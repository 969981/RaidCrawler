//! Switch-side N0/N1 forensic observer for Pokémon Violet 4.0.0 ONLY.
//! No reward edits, no Bag edits and no saved-game modifications.
//! Requires compatible Atmosphère / Skyline on a user-owned test console.
//! The 0x012F serializer callback is NOT proof of Nintendo Internet support.
mod observe;
mod format; // legacy offline layout tests ONLY; never called by the hook

use skyline::hooks::{InlineCtx, Region};
use std::{
    fs::{self, OpenOptions},
    io::Write,
    sync::{
        OnceLock,
        mpsc::{sync_channel, Receiver, SyncSender, TrySendError},
        atomic::{AtomicBool, AtomicU64, Ordering}
    },
    thread
};

const MODE_FILE: &str = "sd:/switch/raidcrawler/hook-mode.txt";
const LOG_FILE: &str = "sd:/switch/raidcrawler/hook.log";
const NATIVE_REWARDS_JSONL: &str = "sd:/switch/raidcrawler/raid-native-rewards.jsonl";
const PATCH_RVA: usize = 0xE32984;
const CHECKS: &[(usize,u32)] = &[
    (0xE32814,0xAA0003F4), (0xE32828,0x52815416),
    (0xE3283C,0xAA0003F7), (0xE32968,0x9101E281),
    (0xE3296C,0x52807902), (0xE32970,0xF9035EEB),
    (0xE32980,0x94982624), (0xE32984,0xAA1503E8),
];

static EVENTS: OnceLock<SyncSender<NativeEvent>> = OnceLock::new();
static IN_CALLBACK: AtomicBool = AtomicBool::new(false);
static HOOK_HITS: AtomicU64 = AtomicU64::new(0);
static QUEUE_DROPPED: AtomicU64 = AtomicU64::new(0);

enum NativeEvent {
    Snapshot(observe::Snapshot),
    Rejected { seq: u64, reason: &'static str },
}

fn log(message: &str) {
    skyline::println!("[RaidCrawler] {}", message);
    if let Ok(mut file) = OpenOptions::new().create(true).append(true).open(LOG_FILE) {
        let _ = writeln!(file, "{}", message);
    }
}

fn emit_event(channel: &SyncSender<NativeEvent>, event: NativeEvent) {
    match channel.try_send(event) {
        Ok(()) => (),
        Err(TrySendError::Full(_)) | Err(TrySendError::Disconnected(_)) => {
            // No disk writes, formatting, locks or blocking in the inline hook.
            QUEUE_DROPPED.fetch_add(1, Ordering::Relaxed);
        }
    }
}

fn worker_loop(receiver: Receiver<NativeEvent>) {
    log("N0_N1_TELEMETRY_WORKER_READY: stdout + SD JSONL, observe only");
    let mut last_dropped = 0;
    loop {
        match receiver.recv_timeout(std::time::Duration::from_secs(2)) {
            Ok(NativeEvent::Rejected {seq, reason}) => {
                log(&format!(
                    "HOOK_HIT seq={} stage=N0 rejected={} (no game modification)",
                    seq, reason));
            }
            Ok(NativeEvent::Snapshot(snapshot)) => {
                let source_stats = observe::stats(&snapshot.source);
                let wire_stats = observe::stats(&snapshot.wire);
                let matches = snapshot.source == snapshot.wire;
                let summary = format!(
                    "HOOK_HIT seq={} stage=N0_N1 kind=0x012F_serializer_candidate \
                    srcRows={} wireRows={} sourceMatchesWire={} \
                    sourceInvalid={} wireInvalid={} trailingEmptyCandidates={} \
                    direction=UNVERIFIED gameWrites=0",
                    snapshot.sequence, source_stats.nonzero_rows, wire_stats.nonzero_rows,
                    matches, source_stats.malformed_rows, wire_stats.malformed_rows,
                    wire_stats.trailing_zero_slots);
                log(&summary);
                let json = observe::snapshot_json(&snapshot);
                if let Ok(mut file) = OpenOptions::new()
                    .create(true).append(true).open(NATIVE_REWARDS_JSONL)
                {
                    if writeln!(file, "{}", json).is_err() {
                        log("N1_JSONL_WRITE_FAILED; no reward modification");
                    }
                } else {
                    log("N1_JSONL_OPEN_FAILED; check /switch/raidcrawler/ directory");
                }
            }
            Err(std::sync::mpsc::RecvTimeoutError::Timeout) => (),
            Err(std::sync::mpsc::RecvTimeoutError::Disconnected) => return,
        }
        let dropped = QUEUE_DROPPED.load(Ordering::Relaxed);
        if dropped != last_dropped {
            log(&format!("N1_EVENTS_DROPPED total={}; data incomplete", dropped));
            last_dropped = dropped;
        }
    }
}

fn verify_code() -> Result<(), &'static str> {
    let text = unsafe { skyline::hooks::getRegionAddress(Region::Text) } as *const u8;
    if text.is_null() { return Err("null .text base"); }
    for &(rva, word) in CHECKS {
        let got = unsafe { (text.add(rva) as *const u32).read_unaligned() };
        if got != word {
            return Err("exact Violet4.0.0 opcode fingerprint mismatch");
        }
    }
    Ok(())
}

/// Non-blocking, bounded N0/N1 read-only callback at main+0xE32984.
/// x20 = serializer's input message object; x23 = 0xAA0 output buffer.
/// Only read reward bytes, not player PK9 or other sensitive buffers.
/// The CPU reaching the instruction is N0 evidence; source-vs-output mirror
/// comparison is N1 evidence. Neither proves the packet reached any guest.
#[skyline::hook(offset = 0xE32984, inline)]
fn raid_bootstrap_after_copy(ctx: &mut InlineCtx) {
    let seq = HOOK_HITS.fetch_add(1, Ordering::Relaxed) + 1;
    if IN_CALLBACK.swap(true, Ordering::AcqRel) {
        QUEUE_DROPPED.fetch_add(1, Ordering::Relaxed);
        return;
    }
    struct Unlock;
    impl Drop for Unlock {
        fn drop(&mut self) { IN_CALLBACK.store(false, Ordering::Release); }
    }
    let _unlock = Unlock;
    let Some(channel) = EVENTS.get() else {
        QUEUE_DROPPED.fetch_add(1, Ordering::Relaxed);
        return;
    };
    let input = ctx.registers[20].x() as *const u8;
    let output = ctx.registers[23].x() as *const u8;
    if input.is_null() || output.is_null()
        || (input as usize & 7) != 0 || (output as usize & 7) != 0
    {
        emit_event(channel, NativeEvent::Rejected {
            seq, reason: "invalid_or_misaligned_input_or_output",
        });
        return;
    }

    // ELF shows the original message allocation is 0x460 bytes and source
    // reward span is x20+[0x13C, 0x40C); output is x23+[0x79C,0xA6C).
    // This memory read is only sound in this exact verified function frame.
    let marker = unsafe {
        std::slice::from_raw_parts(
            output.add(observe::RAIDPOINT_OFFSET), observe::RAIDPOINT_MAGIC.len()
        )
    };
    if marker != observe::RAIDPOINT_MAGIC {
        emit_event(channel, NativeEvent::Rejected {
            seq, reason: "no_RaidPoint_magic_in_native_output",
        });
        return;
    }

    let mut source = [0u8; observe::REWARD_BYTES];
    let mut wire = [0u8; observe::REWARD_BYTES];
    unsafe {
        source.copy_from_slice(std::slice::from_raw_parts(
            input.add(observe::NATIVE_SOURCE_REWARD_OFFSET), observe::REWARD_BYTES));
        wire.copy_from_slice(std::slice::from_raw_parts(
            output.add(observe::WIRE_REWARD_OFFSET), observe::REWARD_BYTES));
    }
    emit_event(channel, NativeEvent::Snapshot(observe::Snapshot::new(seq, source, wire)));
}

#[skyline::main(name = "raidcrawler_sv_reward_hook")]
pub fn main() {
    log("START N0_N1 OBSERVER target=Violet4.0.0; GAME_WRITES=DISABLED");
    if let Err(err) = verify_code() {
        log(&format!("DISABLED {} (no hook installed)",err));
        return;
    }
    log(&format!("ELF_FINGERPRINT verified {}/{}; serializer=main+0x{:X}",
        CHECKS.len(), CHECKS.len(), PATCH_RVA));

    let requested = fs::read_to_string(MODE_FILE).unwrap_or_default();
    if requested.trim().eq_ignore_ascii_case("apply") {
        // Deliberately fail closed; the previous 'apply' meant 720B REPLACE,
        // not lossless charm append. No legacy reward overwrite is allowed.
        log("LEGACY_APPLY_REJECTED: 720B overwrite does not preserve base rewards");
    }
    log("MODE observe-only forced; no reward/template file required");
    let (sender, receiver) = sync_channel::<NativeEvent>(16);
    if EVENTS.set(sender).is_err() {
        log("DISABLED duplicate native observer initialization");
        return;
    }
    if thread::Builder::new().name("raid-observer".to_owned())
        .spawn(move || worker_loop(receiver)).is_err()
    {
        log("DISABLED N0/N1 telemetry thread unavailable");
        return;
    }
    skyline::install_hook!(raid_bootstrap_after_copy);
    log("NATIVE_HOOK_INSTALLED observer only; awaiting actual serializer CPU hit");
    log("N1_PATH sd:/switch/raidcrawler/raid-native-rewards.jsonl");
}
