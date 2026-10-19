//! EXPERIMENTAL native Switch-side A64 inline hook, Violet 4.0.0 ONLY.
//! Game-side plugin, NOT a sys-botbase remote-memory polling process.
//! Compile using cargo-skyline on a supported Switch Rust toolchain.
//! DO NOT claim Nintendo Internet raid support until it is independently observed.
mod format;

use skyline::hooks::{InlineCtx, Region};
use std::{fs, fs::OpenOptions, io::Write, sync::{OnceLock, atomic::{AtomicBool, AtomicU64, Ordering}}};

const CONFIG_FILE: &str = "sd:/switch/raidcrawler/rewards.bin";
const MODE_FILE: &str = "sd:/switch/raidcrawler/hook-mode.txt";
const LOG_FILE: &str = "sd:/switch/raidcrawler/hook.log";
const PATCH_RVA: usize = 0xE32984;
const CHECKS: &[(usize,u32)] = &[
    (0xE32814,0xAA0003F4), (0xE32828,0x52815416),
    (0xE3283C,0xAA0003F7), (0xE32968,0x9101E281),
    (0xE3296C,0x52807902), (0xE32970,0xF9035EEB),
    (0xE32980,0x94982624), (0xE32984,0xAA1503E8),
];

static TEMPLATE: OnceLock<format::RewardTemplate> = OnceLock::new();
static ARMED: AtomicBool = AtomicBool::new(false);
static IN_CALLBACK: AtomicBool = AtomicBool::new(false);
static HOOK_HITS: AtomicU64 = AtomicU64::new(0);
static PATCH_OK: AtomicU64 = AtomicU64::new(0);
static PATCH_FAIL: AtomicU64 = AtomicU64::new(0);

fn log(message: &str) {
    skyline::println!("[RaidCrawler] {}", message);
    // Fail-open logging: SD card logging problems never affect the game.
    if let Ok(mut file) = OpenOptions::new().create(true).append(true).open(LOG_FILE) {
        let _=writeln!(file,"{}",message);
    }
}

fn verify_code() -> Result<(), &'static str> {
    let text=unsafe{skyline::hooks::getRegionAddress(Region::Text)} as *const u8;
    if text.is_null(){return Err("Skyline text region address is null");}
    for &(rva,word) in CHECKS {
        let got=unsafe{(text.add(rva) as *const u32).read_unaligned()};
        if got!=word {return Err("Violet 4.0.0 opcode fingerprint mismatch");}
    }
    Ok(())
}

/// At main+0xE32984, the last memcpy just finished; x23 is the 0xAA0
/// plaintext output. We DO NOT modify registers or jump instructions here.
#[skyline::hook(offset = 0xE32984, inline)]
fn raid_bootstrap_after_copy(ctx: &mut InlineCtx) {
    let hit = HOOK_HITS.fetch_add(1, Ordering::Relaxed) + 1;
    if IN_CALLBACK.swap(true, Ordering::AcqRel) {
        PATCH_FAIL.fetch_add(1, Ordering::Relaxed);
        return; // never recurse into this hook
    }
    struct Unlock;
    impl Drop for Unlock {
        fn drop(&mut self) { IN_CALLBACK.store(false, Ordering::Release); }
    }
    let _unlock=Unlock;

    // This hook fires only when the native serializer reaches this exact
    // instruction. Whether this occurs for Nintendo online must be proven.
    let plaintext = ctx.registers[23].x() as *mut u8;
    if plaintext.is_null() || ((plaintext as usize) & 7) != 0 {
        PATCH_FAIL.fetch_add(1, Ordering::Relaxed);
        log(&format!("HOOK_HIT seq={} rejected invalid x23 output pointer",hit));
        return;
    }

    // x23 is live by verified ELF disassembly at this exact instruction,
    // and the caller allocated 0xAA0 just before copying the RaidPoint.
    let frame = unsafe{std::slice::from_raw_parts_mut(plaintext,format::BOOTSTRAP_BYTES)};
    let marker = &frame[format::POINT_OFFSET..format::POINT_OFFSET+format::RAID_MAGIC.len()];
    if marker != format::RAID_MAGIC {
        PATCH_FAIL.fetch_add(1, Ordering::Relaxed);
        log(&format!("HOOK_HIT seq={} skipped RaidPoint_ magic mismatch",hit));
        return;
    }

    if !ARMED.load(Ordering::Acquire) {
        log(&format!("HOOK_HIT seq={} mode=observe (no patch)",hit));
        return;
    }
    let Some(template)=TEMPLATE.get() else {
        PATCH_FAIL.fetch_add(1, Ordering::Relaxed);
        log(&format!("HOOK_HIT seq={} no valid reward template",hit));
        return;
    };
    let start=format::REWARD_OFFSET;
    let stop=start+format::REWARD_BYTES;
    let mut backup=[0u8;format::REWARD_BYTES];
    backup.copy_from_slice(&frame[start..stop]);
    // The only modified bytes belong to this call's 45 reward rows.
    match format::patch_bootstrap(frame,template) {
        Ok(()) => {
            if frame[start..stop] == template.data {
                PATCH_OK.fetch_add(1, Ordering::Relaxed);
                log(&format!("HOOK_HIT seq={} PATCH_VERIFIED rows={} checksum={:016X}",
                    hit,template.configured,template.checksum));
            } else {
                frame[start..stop].copy_from_slice(&backup);
                PATCH_FAIL.fetch_add(1,Ordering::Relaxed);
                log(&format!("HOOK_HIT seq={} PATCH_VERIFY_FAILED rollback=OK",hit));
            }
        },
        Err(err) => {
            frame[start..stop].copy_from_slice(&backup);
            PATCH_FAIL.fetch_add(1,Ordering::Relaxed);
            log(&format!("HOOK_HIT seq={} PATCH_REJECTED={} rollback=OK",hit,err));
        }
    }
}

#[skyline::main(name = "raidcrawler_sv_reward_hook")]
pub fn main() {
    log("START native module target=Violet4.0.0; status=preflight");
    match verify_code() {
        Ok(()) => log("ELF_FINGERPRINT verified 8/8; serializer offset=0xE32984"),
        Err(err) => {
            log(&format!("DISABLED {}; no hook installed",err));
            return;
        }
    }

    match fs::read(CONFIG_FILE) {
        Ok(data)=>match format::RewardTemplate::decode(&data) {
            Ok(template)=>{
                log(&format!("CONFIG_VALID rows={} bytes=720 checksum={:016X}",
                    template.configured,template.checksum));
                if TEMPLATE.set(template).is_err() {
                    log("CONFIG_DUPLICATE load refused");
                    return;
                }
            },
            Err(err)=>log(&format!("CONFIG_INVALID {}",err)),
        },
        Err(err)=>log(&format!("CONFIG_MISSING {} ({})",CONFIG_FILE,err)),
    }
    // Require an explicit opt-in file. An unconfigured plugin observes only.
    let apply = fs::read_to_string(MODE_FILE).map(|s|s.trim().eq_ignore_ascii_case("apply")).unwrap_or(false);
    let armed=apply && TEMPLATE.get().is_some();
    ARMED.store(armed,Ordering::Release);
    log(if armed {"MODE apply: reward overwrite armed"} else {"MODE observe: no game reward changes"});
    // Signature already validated, now install a real ARM64 instruction hook.
    skyline::install_hook!(raid_bootstrap_after_copy);
    log("NATIVE_HOOK_INSTALLED: callback will report HOOK_HIT on serializer execution");
}
