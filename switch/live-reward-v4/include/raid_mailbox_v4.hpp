#pragma once
// Mailbox V4 native contract: pure, read-only, independently compilable C++17.
// This file DOES NOT install a Hook, allocate an executable cave, or change
// game rewards. It will be reused by a future, separately gated subsdk9 probe.
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace raidv4 {
constexpr std::size_t kMailboxLength = 0x800;
constexpr std::size_t kHeaderLength = 0x40;
constexpr std::size_t kSlotLength = 0x3C0;
constexpr std::size_t kRows = 45;
constexpr std::size_t kRowLength = 16;
constexpr std::uint32_t kNoSlot = 0xFFFFFFFFu;

struct alignas(8) SlotV4 { std::uint8_t bytes[kSlotLength]{}; };
struct alignas(8) MailboxV4 {
    char magic[8] = {'R','C','H','M','B','0','4','\0'};
    std::uint32_t version = 4;
    std::uint32_t length = kMailboxLength;
    std::uint32_t capabilities = 1; // ONLY read-only observer
    std::uint32_t status = 1;
    std::atomic<std::uint32_t> activeSlot{kNoSlot};
    std::uint32_t reserved = 0;
    std::atomic<std::uint64_t> publishedRevision{0};
    std::atomic<std::uint64_t> observedRevision{0};
    std::atomic<std::uint32_t> observationCount{0};
    std::atomic<std::uint32_t> lastObservationStatus{0};
    std::atomic<std::uint32_t> generation{0};
    std::uint32_t reserved2 = 0;
    SlotV4 slots[2]{};
    std::uint8_t tail[0x40]{};
};
static_assert(sizeof(SlotV4) == 0x3C0, "slot ABI");
static_assert(offsetof(MailboxV4, capabilities) == 0x10, "header ABI");
static_assert(offsetof(MailboxV4, activeSlot) == 0x18, "selector ABI");
static_assert(offsetof(MailboxV4, publishedRevision) == 0x20, "revision ABI");
static_assert(offsetof(MailboxV4, observedRevision) == 0x28, "observation ABI");
static_assert(offsetof(MailboxV4, slots) == 0x40, "slot A ABI");
static_assert(offsetof(MailboxV4, tail) == 0x7C0, "slot B ABI");
static_assert(sizeof(MailboxV4) == kMailboxLength, "mailbox ABI");

inline std::uint32_t U32(const std::uint8_t* p) {
    return std::uint32_t(p[0]) | (std::uint32_t(p[1]) << 8) |
           (std::uint32_t(p[2]) << 16) | (std::uint32_t(p[3]) << 24);
}
inline std::uint64_t U64(const std::uint8_t* p) {
    return std::uint64_t(U32(p)) | (std::uint64_t(U32(p + 4)) << 32);
}
inline std::uint32_t Checksum(const std::uint8_t* slot) {
    std::uint32_t hash = 2166136261u;
    for (std::size_t i = 0; i < kSlotLength; ++i) {
        const std::uint8_t value =
            i >= 0x18 && i < 0x1C ? 0 : slot[i]; // CRC field treated as zero
        hash = (hash ^ value) * 16777619u;
    }
    return hash;
}
inline bool Validate(const std::uint8_t* s, std::uint64_t expectedRevision) {
    if (expectedRevision == 0 || U64(s) != expectedRevision) return false;
    if (U32(s + 0x18) != Checksum(s)) return false;
    if (U32(s + 8) > 1 || U32(s + 0xC) != 1 || U32(s + 0x10) != 2) return false;
    const std::uint32_t count = U32(s + 0x14);
    if (count > kRows || (U32(s + 8) != 0 && count == 0) ||
        U32(s + 0x1C) != 0) return false;
    for (std::uint32_t i = 0; i < count; ++i) {
        const auto* row = s + 0x20 + i * 16;
        const auto id = U32(row);
        if (id == 0 || id > 65535 || U32(row+4) == 0 ||
            U32(row+4) > 999 || U32(row+8) != 0 || U32(row+12) != 5)
            return false;
        for (std::uint32_t j = 0; j < i; ++j)
            if (U32(s + 0x20 + j * 16) == id) return false;
    }
    for (std::size_t i = 0x20 + count * kRowLength; i < kSlotLength; ++i)
        if (s[i] != 0) return false;
    return true;
}

struct SnapshotV4 {
    std::uint8_t slot[kSlotLength]{};
    std::uint64_t revision = 0;
    std::uint32_t index = kNoSlot;
};

// Return value refers ONLY to the config. It is not proof that any packet was
// sent, nor that a reward was applied. Corrupt/racing snapshots fail closed.
inline bool ObserveReadOnly(MailboxV4& mailbox, SnapshotV4& snapshot) {
    mailbox.observationCount.fetch_add(1, std::memory_order_relaxed);
    const std::uint32_t slot = mailbox.activeSlot.load(std::memory_order_acquire);
    const std::uint64_t revision =
        mailbox.publishedRevision.load(std::memory_order_acquire);
    if (slot > 1 || revision == 0) {
        mailbox.lastObservationStatus.store(2, std::memory_order_release);
        return false;
    }
    std::memcpy(snapshot.slot, mailbox.slots[slot].bytes, kSlotLength);
    std::atomic_thread_fence(std::memory_order_acquire);
    if (mailbox.activeSlot.load(std::memory_order_acquire) != slot ||
        mailbox.publishedRevision.load(std::memory_order_acquire) != revision) {
        mailbox.lastObservationStatus.store(4, std::memory_order_release);
        return false;
    }
    if (!Validate(snapshot.slot, revision)) {
        mailbox.lastObservationStatus.store(3, std::memory_order_release);
        return false;
    }
    snapshot.revision = revision;
    snapshot.index = slot;
    mailbox.observedRevision.store(revision, std::memory_order_release);
    mailbox.lastObservationStatus.store(1, std::memory_order_release);
    return true;
}
} // namespace raidv4
