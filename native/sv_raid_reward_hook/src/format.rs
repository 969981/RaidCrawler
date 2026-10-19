// Portable, testable serialization checks for 45 × 16-byte RaidPoint rewards.
// No Switch-specific dependencies; native plugin includes this module unchanged.
pub const SLOT_COUNT: usize = 45;
pub const SLOT_SIZE: usize = 16;
pub const REWARD_BYTES: usize = SLOT_COUNT * SLOT_SIZE;
pub const BOOTSTRAP_BYTES: usize = 0xAA0;
pub const POINT_OFFSET: usize = 0x6B8;
pub const REWARD_OFFSET: usize = POINT_OFFSET + 0xE4;
pub const RAID_MAGIC: &[u8] = b"RaidPoint_";

#[derive(Clone)]
pub struct RewardTemplate {
    pub data: [u8; REWARD_BYTES],
    pub configured: usize,
    pub checksum: u64,
}

impl RewardTemplate {
    pub fn decode(raw: &[u8]) -> Result<Self, &'static str> {
        if raw.len() != REWARD_BYTES { return Err("expected 720 bytes"); }
        let mut buf = [0u8; REWARD_BYTES];
        buf.copy_from_slice(raw);
        let mut count = 0;
        for slot in buf.chunks_exact(SLOT_SIZE) {
            if slot.iter().all(|&v| v == 0) { continue; }
            let item = u32::from_le_bytes(slot[0..4].try_into().unwrap());
            let quantity = u32::from_le_bytes(slot[4..8].try_into().unwrap());
            let rare = u32::from_le_bytes(slot[8..12].try_into().unwrap());
            let subject = u32::from_le_bytes(slot[12..16].try_into().unwrap());
            if !(1..=u16::MAX as u32).contains(&item) {
                return Err("invalid item ID in reward row");
            }
            if !(1..=999).contains(&quantity) {
                return Err("invalid quantity in reward row");
            }
            if rare > 1 || subject > 5 {
                return Err("invalid rare/subject in reward row");
            }
            count += 1;
        }
        if count == 0 { return Err("no configured reward rows"); }
        Ok(Self { data: buf, configured: count, checksum: checksum(&buf) })
    }
}
pub fn checksum(bytes: &[u8]) -> u64 {
    let mut h = 0xcbf29ce484222325u64;
    for &b in bytes {
        h ^= b as u64;
        h = h.wrapping_mul(0x100000001b3);
    }
    h
}

/// Apply to one already-constructed 0xAA0 uncompressed bootstrap.
/// Does not compact rows, and leaves every byte outside rewards unchanged.
pub fn patch_bootstrap(frame: &mut [u8], template: &RewardTemplate) -> Result<(), &'static str> {
    if frame.len() != BOOTSTRAP_BYTES { return Err("incorrect bootstrap length"); }
    if &frame[POINT_OFFSET..POINT_OFFSET + RAID_MAGIC.len()] != RAID_MAGIC {
        return Err("RaidPoint_ marker mismatch");
    }
    frame[REWARD_OFFSET..REWARD_OFFSET + REWARD_BYTES].copy_from_slice(&template.data);
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    fn fixture() -> Vec<u8> {
        let mut bytes: Vec<u8> = (0..BOOTSTRAP_BYTES).map(|i| (i.wrapping_mul(19) % 256) as u8).collect();
        bytes[POINT_OFFSET..POINT_OFFSET+RAID_MAGIC.len()].copy_from_slice(RAID_MAGIC);
        bytes
    }
    fn sample() -> RewardTemplate {
        let mut rows = [0u8;REWARD_BYTES];
        rows[0..4].copy_from_slice(&1u32.to_le_bytes());
        rows[4..8].copy_from_slice(&999u32.to_le_bytes());
        rows[12..16].copy_from_slice(&5u32.to_le_bytes());
        rows[44*16..44*16+4].copy_from_slice(&796u32.to_le_bytes());
        rows[44*16+4..44*16+8].copy_from_slice(&100u32.to_le_bytes());
        rows[44*16+12..44*16+16].copy_from_slice(&4u32.to_le_bytes());
        RewardTemplate::decode(&rows).unwrap()
    }
    #[test]
    fn checks_template_fields_and_holes() {
        let t = sample();
        assert_eq!(t.configured, 2);
        assert_eq!(t.data.len(), 720);
        let mut malformed=t.data.clone();
        malformed[12..16].copy_from_slice(&6u32.to_le_bytes());
        assert!(RewardTemplate::decode(&malformed).is_err());
        malformed=t.data.clone();
        malformed[4..8].copy_from_slice(&1000u32.to_le_bytes());
        assert!(RewardTemplate::decode(&malformed).is_err());
    }
    #[test]
    fn patches_only_reward_block_and_preserves_25_holes() {
        let mut source=fixture();
        let unchanged=source.clone();
        let t=sample();
        patch_bootstrap(&mut source, &t).unwrap();
        assert_eq!(&source[..REWARD_OFFSET], &unchanged[..REWARD_OFFSET]);
        assert_eq!(&source[REWARD_OFFSET+720..], &unchanged[REWARD_OFFSET+720..]);
        assert_eq!(&source[REWARD_OFFSET..REWARD_OFFSET+720], &t.data);
        assert!(source[REWARD_OFFSET+16..REWARD_OFFSET+44*16].iter().all(|v|*v==0));
    }
    #[test]
    fn rejects_bad_marker_and_wrong_length() {
        let t=sample();
        assert!(patch_bootstrap(&mut vec![0;BOOTSTRAP_BYTES], &t).is_err());
        assert!(patch_bootstrap(&mut fixture()[..BOOTSTRAP_BYTES-1], &t).is_err());
    }
}
