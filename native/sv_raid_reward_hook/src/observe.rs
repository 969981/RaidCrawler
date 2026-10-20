//! Portable N0/N1 telemetry for the real Switch host's 0x012F serializer.
//! NEVER writes game memory. Does not handle PK9, player names or account IDs.
//! The raw native callback must not perform JSON formatting or SD I/O.
use std::fmt::Write as _;

pub const SLOTS: usize = 45;
pub const ROW_BYTES: usize = 16;
pub const REWARD_BYTES: usize = SLOTS * ROW_BYTES;
pub const BOOTSTRAP_BYTES: usize = 0xAA0;
pub const RAIDPOINT_OFFSET: usize = 0x6B8;
pub const WIRE_REWARD_OFFSET: usize = RAIDPOINT_OFFSET + 0xE4; // 0x79C
pub const NATIVE_SOURCE_REWARD_OFFSET: usize = 0x13C; // input message object's x20
pub const RAIDPOINT_MAGIC: &[u8] = b"RaidPoint_";

#[derive(Clone)]
pub struct Snapshot {
    pub sequence: u64,
    pub source: [u8; REWARD_BYTES],
    pub wire: [u8; REWARD_BYTES],
}
impl Snapshot {
    pub fn new(sequence: u64, source: [u8; REWARD_BYTES], wire: [u8; REWARD_BYTES]) -> Self {
        Self { sequence, source, wire }
    }
}

#[derive(Default, Clone, Copy, Debug, PartialEq, Eq)]
pub struct Stats {
    pub nonzero_rows: usize,
    pub zero_rows: usize,
    pub malformed_rows: usize,
    pub embedded_zero_holes: usize,
    pub trailing_zero_slots: usize,
    pub subjects: [usize; 6],
}
pub fn hash_fnv1a64(bytes: &[u8]) -> u64 {
    bytes.iter().fold(0xcbf29ce484222325u64, |h, x| {
        (h ^ u64::from(*x)).wrapping_mul(0x100000001b3)
    })
}
fn four_u32(b: &[u8]) -> [u32; 4] {
    assert_eq!(b.len(), ROW_BYTES);
    [
        u32::from_le_bytes(b[0..4].try_into().unwrap()),
        u32::from_le_bytes(b[4..8].try_into().unwrap()),
        u32::from_le_bytes(b[8..12].try_into().unwrap()),
        u32::from_le_bytes(b[12..16].try_into().unwrap()),
    ]
}
pub fn stats(bytes: &[u8; REWARD_BYTES]) -> Stats {
    let mut out = Stats::default();
    let mut seen_zero = false;
    for row in bytes.chunks_exact(ROW_BYTES) {
        if row.iter().all(|v| *v == 0) {
            out.zero_rows += 1;
            seen_zero = true;
            continue;
        }
        out.nonzero_rows += 1;
        if seen_zero {
            out.embedded_zero_holes += 1; // no implicit "safe free slot" claim
        }
        let [item, quantity, rare, subject] = four_u32(row);
        if !(1..=65535).contains(&item) || !(1..=999).contains(&quantity)
            || rare > 1 || subject > 5
        {
            out.malformed_rows += 1; // report raw bytes, do not modify
        }
        if subject <= 5 {
            out.subjects[subject as usize] += 1;
        }
    }
    for row in bytes.chunks_exact(ROW_BYTES).rev() {
        if row.iter().all(|v| *v == 0) {
            out.trailing_zero_slots += 1;
        } else {
            break;
        }
    }
    out
}
fn rows_json(output: &mut String, bytes: &[u8; REWARD_BYTES]) {
    output.push('[');
    for (i, row) in bytes.chunks_exact(ROW_BYTES).enumerate() {
        let [item, qty, rare, subject] = four_u32(row);
        if i != 0 { output.push(','); }
        let state = if row.iter().all(|v| *v == 0) { "ZERO_CANDIDATE" }
            else if item == 0 || item > 65535 || qty == 0 || qty > 999
                || rare > 1 || subject > 5 { "NONZERO_UNRECOGNIZED" }
            else { "NONZERO" };
        write!(output,
            "{{\"slot\":{},\"itemId\":{},\"quantity\":{},\"rare\":{},\"subject\":{},\"state\":\"{}\"}}",
            i, item, qty, rare, subject, state).unwrap();
    }
    output.push(']');
}

/// One JSONL event per native callback, rendered only by a worker thread.
/// Contains only 45*2 numeric reward rows, not any PK9, network address or username.
pub fn snapshot_json(snapshot: &Snapshot) -> String {
    let a = stats(&snapshot.source);
    let b = stats(&snapshot.wire);
    let matches = snapshot.source == snapshot.wire;
    let mut result = String::with_capacity(7500);
    write!(result,
        "{{\"event\":\"N1_NATIVE_RAID_REWARD_SNAPSHOT\",\"seq\":{},\"version\":\"Violet4.0.0\",\
         \"provenance\":\"NATIVE_SERIALIZER_CALLBACK_NOT_RECEIVER_ATTESTED\",\
         \"direction\":\"UNVERIFIED_FROM_CALLBACK_ALONE\",\
         \"sourceAddress\":\"x20+0x13C\",\"wireAddress\":\"x23+0x79C\",\
         \"rewardSlots\":45,\"recordBytes\":16,\"originalSourceMatchesWire\":{},\
         \"sourceFnv64\":\"{:016X}\",\"wireFnv64\":\"{:016X}\"",
        snapshot.sequence, matches, hash_fnv1a64(&snapshot.source), hash_fnv1a64(&snapshot.wire)).unwrap();
    write!(result,
        ",\"sourceStats\":{{\"nonzero\":{},\"allZero\":{},\"nonzeroUnrecognized\":{},\
         \"holesAfterFirstZero\":{},\"trailingZeroCandidates\":{},\"subjects\":{:?}}}",
        a.nonzero_rows,a.zero_rows,a.malformed_rows,a.embedded_zero_holes,a.trailing_zero_slots,a.subjects).unwrap();
    write!(result,
        ",\"wireStats\":{{\"nonzero\":{},\"allZero\":{},\"nonzeroUnrecognized\":{},\
         \"holesAfterFirstZero\":{},\"trailingZeroCandidates\":{},\"subjects\":{:?}}}",
        b.nonzero_rows,b.zero_rows,b.malformed_rows,b.embedded_zero_holes,b.trailing_zero_slots,b.subjects).unwrap();
    result.push_str(",\"sourceRows\":");
    rows_json(&mut result, &snapshot.source);
    result.push_str(",\"wireRows\":");
    rows_json(&mut result, &snapshot.wire);
    result.push_str(",\"memoryWritten\":false,\"hookMode\":\"observe-only\",\
         \"readyForAppend\":false,\"guestReceiptVerified\":false,\"bagSaveVerified\":false}");
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    fn row(buffer: &mut [u8; REWARD_BYTES], i: usize, item: u32, qty: u32, subject: u32) {
        let at = i * ROW_BYTES;
        buffer[at..at+4].copy_from_slice(&item.to_le_bytes());
        buffer[at+4..at+8].copy_from_slice(&qty.to_le_bytes());
        buffer[at+12..at+16].copy_from_slice(&subject.to_le_bytes());
    }
    #[test]
    fn native_source_and_bootstrap_offsets_are_distinct() {
        assert_eq!(WIRE_REWARD_OFFSET, 0x79C);
        assert_eq!(NATIVE_SOURCE_REWARD_OFFSET, 0x13C);
        assert_eq!(WIRE_REWARD_OFFSET + REWARD_BYTES, 0xA6C);
        assert_eq!(BOOTSTRAP_BYTES, 0xAA0);
    }
    #[test]
    fn preserves_opaque_nonzero_and_detects_nonterminal_holes() {
        let mut data = [0;REWARD_BYTES];
        row(&mut data, 0, 1128, 1, 0);
        row(&mut data, 3, 492, 999, 5);
        data[44*16] = 0xFF; // unknown metadata, not a free slot
        let report = stats(&data);
        assert_eq!(report.nonzero_rows, 3);
        assert_eq!(report.malformed_rows, 1);
        assert_eq!(report.embedded_zero_holes, 2);
        assert_eq!(report.trailing_zero_slots, 0);
        assert_eq!(report.subjects[5],1);
    }
    #[test]
    fn json_has_all_native_rows_but_no_pk9_and_no_receiver_claim() {
        let mut source = [0;REWARD_BYTES];
        row(&mut source, 0, 492, 1, 5);
        let result = snapshot_json(&Snapshot::new(12,source,source));
        assert!(result.contains("\"originalSourceMatchesWire\":true"));
        assert!(result.contains("\"sourceRows\":[{\"slot\":0,\"itemId\":492,\"quantity\":1"));
        assert!(result.contains("\"slot\":44"));
        assert!(result.contains("\"guestReceiptVerified\":false"));
        assert!(result.contains("\"direction\":\"UNVERIFIED_FROM_CALLBACK_ALONE\""));
        assert!(!result.contains("pk9"));
    }
    #[test]
    fn source_wire_mismatch_is_visible_not_silently_fixed() {
        let mut a = [0;REWARD_BYTES];
        let mut b = [0;REWARD_BYTES];
        row(&mut a,0,1,1,0);
        row(&mut b,0,1,999,0);
        let json = snapshot_json(&Snapshot::new(1,a,b));
        assert!(json.contains("\"originalSourceMatchesWire\":false"));
        assert_ne!(hash_fnv1a64(&a),hash_fnv1a64(&b));
    }
}
