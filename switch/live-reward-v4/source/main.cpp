// RaidCrawler Mailbox V4: N1 read-only capture for Violet 4.0.0.
// The only game memory access is READ. There is no reward mutation code,
// no opt-in N2 writer, and no network service beyond the existing sys-botbase.
#include "lib.hpp"
#include "raid_mailbox_v4.hpp"
#include <atomic>
#include <cstdint>
#include <cstring>
#include <cstdio>

namespace rcv4 {
constexpr uintptr_t kHookRva = 0xE32984;
constexpr size_t kSourceRaidPoint = 0x58;
constexpr size_t kDestinationRaidPoint = 0x6B8;
constexpr size_t kRewardInPoint = 0xE4;
constexpr size_t kRewardBytes = 45 * 16;
constexpr size_t kPointBytes = 0x3E8;
constexpr uint64_t kWireSize = 0xAA0;
constexpr char kPrefix[] = "RaidPoint_";
static_assert(kDestinationRaidPoint + kPointBytes == kWireSize);
static_assert(kRewardInPoint + kRewardBytes == 0x3B4);

alignas(64) raidv4::MailboxV4 g_mailbox;
std::atomic_flag g_captureBusy = ATOMIC_FLAG_INIT;

struct OpcodeGuard { uintptr_t rva; uint32_t code; };
constexpr OpcodeGuard kFingerprint[] = {
    {0xE327FC,0xA9BB7BFD},{0xE32814,0xAA0003F4},
    {0xE32828,0x52815416},{0xE3283C,0xAA0003F7},
    {0xE32844,0x52802B18},{0xE32960,0x911B62E0},
    {0xE32964,0xA945AA8B},{0xE32968,0x9101E281},
    {0xE3296C,0x52807902},{0xE32970,0xF9035EEB},
    {0xE32980,0x94982624},{0xE32984,0xAA1503E8},
    {0xE32988,0xAA1403E0},{0xE32990,0xAA1703E1},
    {0xE32994,0x2A1603E2},{0xE329A0,0x52800024},
    {0xE329B0,0x141D8073},
};

bool IsMatchingOpcodeSet() {
    const uintptr_t base = exl::util::modules::GetTargetStart();
    if (base < 0x10000) return false;
    for (const OpcodeGuard& op : kFingerprint) {
        if (*reinterpret_cast<const volatile uint32_t*>(base + op.rva) != op.code)
            return false;
    }
    return true;
}

uint32_t Fnv1a(const uint8_t* data, size_t count) {
    uint32_t hash = 2166136261u;
    for (size_t i = 0; i < count; ++i)
        hash = (hash ^ data[i]) * 16777619u;
    return hash;
}

// Tail: mailbox + 0x7C0, Native-only; atomically snapshot via header generation.
// 0x7C0 captureSerial, 0x7C4 result, 0x7C8 source FNV32, 0x7CC outgoing FNV32.
// Results 1=valid, 2=bad-context, 3=bad-point or not equal, 4=no-config,
//         5=invalid-config, 6=observed-config. None mean reward delivery.
void Report(const uint32_t status, const uint32_t fromHash,
            const uint32_t toHash) {
    g_mailbox.generation.fetch_add(1, std::memory_order_acq_rel); // odd
    auto* tail = g_mailbox.tail;
    const auto serial = raidv4::U32(tail) + 1;
    auto put = [](uint8_t* dst, uint32_t value) {
        for (int i = 0; i < 4; ++i)
            dst[i] = static_cast<uint8_t>(value >> (i * 8));
    };
    put(tail, serial);
    put(tail + 4, status);
    put(tail + 8, fromHash);
    put(tail + 12, toHash);
    g_mailbox.generation.fetch_add(1, std::memory_order_release); // even
}

void Capture(exl::hook::InlineCtx* ctx) {
    if (g_mailbox.status != 1) return;
    if (g_captureBusy.test_and_set(std::memory_order_acquire)) return;
    const uintptr_t source = static_cast<uintptr_t>(ctx->X[20]);
    const uintptr_t output = static_cast<uintptr_t>(ctx->X[23]);
    const bool contextOk = source >= 0x10000 && output >= 0x10000 &&
        (source & 7) == 0 && (output & 7) == 0 &&
        (ctx->X[22] & 0xffffffffUL) == kWireSize;
    if (!contextOk) {
        Report(2, 0, 0);
        g_captureBusy.clear(std::memory_order_release);
        return;
    }

    const auto* from = reinterpret_cast<const uint8_t*>(source + kSourceRaidPoint);
    const auto* to = reinterpret_cast<const uint8_t*>(output + kDestinationRaidPoint);
    // IMPORTANT: all addresses here are READ ONLY. The producer has already
    // finished copying 0x3E8 bytes. No writes to x20/x23, ever.
    if (std::memcmp(to, kPrefix, sizeof(kPrefix) - 1) != 0 ||
        std::memcmp(from, to, kPointBytes) != 0) {
        Report(3, 0, 0);
        g_captureBusy.clear(std::memory_order_release);
        return;
    }
    const uint32_t fromHash =
        Fnv1a(from + kRewardInPoint, kRewardBytes);
    const uint32_t toHash =
        Fnv1a(to + kRewardInPoint, kRewardBytes);
    raidv4::SnapshotV4 snapshot{};
    const bool valid = raidv4::ObserveReadOnly(g_mailbox, snapshot);
    // Only reports validity/readback; not a simulation of actual append!
    Report(valid ? 6 : (g_mailbox.lastObservationStatus.load(
        std::memory_order_acquire) == 2 ? 4 : 5), fromHash, toHash);
    g_captureBusy.clear(std::memory_order_release);
}

void Log(const char* string) { svcOutputDebugString(string, std::strlen(string)); }
} // namespace rcv4

HOOK_DEFINE_INLINE(RaidV4ReadOnly) {
    static void Callback(exl::hook::InlineCtx* ctx) {
        rcv4::Capture(ctx);
    }
};

extern "C" void exl_main(void*, void*) {
    char msg[160]{};
    std::snprintf(msg, sizeof(msg),
        "[RC-V4-N1] mailbox=0x%016llX readonly=1 length=0x800",
        static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(&rcv4::g_mailbox)));
    rcv4::Log(msg);
    // Status=2 means incompatible fingerprint / hook not installed.
    if (!rcv4::IsMatchingOpcodeSet()) {
        rcv4::g_mailbox.status = 2;
        rcv4::Log("[RC-V4-N1] opcode mismatch; hook not installed");
        return;
    }
    exl::hook::Initialize();
    RaidV4ReadOnly::InstallAtOffset(rcv4::kHookRva);
    rcv4::Log("[RC-V4-N1] readonly hook installed; write capability absent");
}

extern "C" NORETURN void exl_exception_entry() {
    EXL_ABORT("[RC-V4-N1] exlaunch exception");
}
