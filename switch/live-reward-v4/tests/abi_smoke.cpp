#include "../include/raid_mailbox_v4.hpp"
#include <cassert>
#include <cstdint>
#include <iostream>
#include <cstring>

static void Put32(std::uint8_t* p, std::uint32_t n) {
    for (int i = 0; i < 4; i++) p[i] = static_cast<std::uint8_t>(n >> (i*8));
}
static void Put64(std::uint8_t* p, std::uint64_t n) {
    Put32(p, static_cast<std::uint32_t>(n));
    Put32(p + 4, static_cast<std::uint32_t>(n >> 32));
}
static void Populate(std::uint8_t* s, std::uint64_t rev, std::uint32_t id) {
    std::memset(s, 0, raidv4::kSlotLength);
    Put64(s, rev);
    Put32(s + 8, 1);        // enabled
    Put32(s + 0xC, 1);      // append
    Put32(s + 0x10, 2);     // persistent
    Put32(s + 0x14, 1);     // one row
    Put32(s + 0x20, id);
    Put32(s + 0x24, 999);
    Put32(s + 0x2C, 5);
    Put32(s + 0x18, raidv4::Checksum(s));
}

int main() {
    raidv4::MailboxV4 mailbox;
    raidv4::SnapshotV4 sample;
    assert(sizeof(mailbox) == 0x800);
    assert(!raidv4::ObserveReadOnly(mailbox, sample));
    assert(mailbox.lastObservationStatus.load() == 2);
    Populate(mailbox.slots[0].bytes, 1, 492);
    // Golden vector for revision=1 / item=492 / quantity=999 / subject=5:
    // cross-language fixed CRC (calculated byte-wise with checksum word 0).
    assert(raidv4::Checksum(mailbox.slots[0].bytes) == 0x3FD71947u);
    mailbox.publishedRevision.store(1, std::memory_order_release);
    mailbox.activeSlot.store(0, std::memory_order_release);
    assert(raidv4::ObserveReadOnly(mailbox, sample));
    assert(sample.revision == 1 && sample.index == 0);
    assert(raidv4::U32(sample.slot + 0x20) == 492);
    assert(mailbox.observedRevision.load() == 1);
    Populate(mailbox.slots[1].bytes, 2, 493);
    mailbox.publishedRevision.store(2, std::memory_order_release);
    mailbox.activeSlot.store(1, std::memory_order_release);
    assert(raidv4::ObserveReadOnly(mailbox, sample));
    assert(sample.revision == 2 && sample.index == 1);
    assert(raidv4::U32(sample.slot + 0x20) == 493);
    // Fail closed on corrupted slot, no update of observedRevision.
    mailbox.slots[1].bytes[0x24] ^= 0x40;
    assert(!raidv4::ObserveReadOnly(mailbox, sample));
    assert(mailbox.observedRevision.load() == 2);
    assert(mailbox.lastObservationStatus.load() == 3);
    // Fail closed during half commit, before selector is published.
    mailbox.publishedRevision.store(3);
    assert(!raidv4::ObserveReadOnly(mailbox, sample));
    std::cout << "V4 native read-only ABI/observer smoke tests passed\n";
}
