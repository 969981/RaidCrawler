// RaidCrawler host N1 - read-only bootstrap observer, in-RAM sys-botbase mailbox.
// Only Violet 4.0.0. Hook installation patches executable code; the callback
// NEVER writes game reward, network, bag, or save data. No filesystem or socket.
#include "lib.hpp"
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace rcprobe {
constexpr uintptr_t HookRva = 0xE32984; // after original serializer memcpy
constexpr size_t RowBytes = 45 * 16;     // 720, aligned from actual donor
constexpr size_t SourceRows = 0x58 + 0xE0; // x20 + 0x138
constexpr size_t WireRows = 0x6B8 + 0xE0;  // x23 + 0x798
constexpr uint32_t ProtocolVersion = 1;
constexpr uint32_t MailboxBytes = 0x600;
static_assert(SourceRows == 0x138 && WireRows == 0x798);
static_assert(0x798 + RowBytes == 0xA68);

struct Mailbox {
    char magic[8] = {'R','C','H','M','B','0','1','\0'}; // +0x00
    uint32_t version = ProtocolVersion;              // +0x08
    uint32_t length = MailboxBytes;                  // +0x0C
    std::atomic<uint32_t> enabled{0};               // +0x10, PC writes only here
    std::atomic<uint32_t> status{0};                // +0x14, 1 = hook installed
    std::atomic<uint32_t> count{0};                 // +0x18, snapshots
    std::atomic<uint32_t> dropped{0};               // +0x1C
    std::atomic<uint32_t> generation{0};            // +0x20, odd = update
    uint32_t reserved = 0;                         // +0x24
    uint64_t tick = 0;                             // +0x28
    uint64_t session = 0;                          // +0x30, unused (zero)
    uint64_t metadata = 0;                         // +0x38, unused (zero)
    uint8_t source[RowBytes]{};                     // +0x40
    uint8_t bootstrap[RowBytes]{};                  // +0x310
    uint8_t pad[0x20]{};                           // 0x5E0..0x5FF
};
static_assert(offsetof(Mailbox, enabled) == 0x10);
static_assert(offsetof(Mailbox, status) == 0x14);
static_assert(offsetof(Mailbox, count) == 0x18);
static_assert(offsetof(Mailbox, generation) == 0x20);
static_assert(offsetof(Mailbox, tick) == 0x28);
static_assert(offsetof(Mailbox, source) == 0x40);
static_assert(offsetof(Mailbox, bootstrap) == 0x310);
static_assert(sizeof(Mailbox) == MailboxBytes);

alignas(64) static Mailbox g_mailbox;
static std::atomic_flag g_writer = ATOMIC_FLAG_INIT;

struct OpcodeGuard { uintptr_t rva; uint32_t word; };
constexpr OpcodeGuard Fingerprint[] = {
    {0xE327FC,0xA9BB7BFD},{0xE32814,0xAA0003F4},
    {0xE32828,0x52815416},{0xE3283C,0xAA0003F7},
    {0xE32844,0x52802B18},{0xE32960,0x911B62E0},
    {0xE32964,0xA945AA8B},{0xE32968,0x9101E281},
    {0xE3296C,0x52807902},{0xE32970,0xF9035EEB},
    {0xE32980,0x94982624},{0xE329B0,0x141D8073},
};
static bool VerifyGameFingerprint() {
    uintptr_t start = exl::util::modules::GetTargetStart();
    for (const auto &entry : Fingerprint) {
        if (*reinterpret_cast<const volatile uint32_t*>(start+entry.rva) != entry.word)
            return false;
    }
    return true;
}
static void Log(const char* msg) { svcOutputDebugString(msg, std::strlen(msg)); }

static void Capture(exl::hook::InlineCtx* ctx) {
    if (g_mailbox.enabled.load(std::memory_order_acquire) != 1 ||
        g_mailbox.status.load(std::memory_order_relaxed) != 1)
        return;
    if (g_writer.test_and_set(std::memory_order_acquire)) {
        g_mailbox.dropped.fetch_add(1, std::memory_order_relaxed);
        return;
    }
    const uintptr_t in = static_cast<uintptr_t>(ctx->X[20]);
    const uintptr_t out = static_cast<uintptr_t>(ctx->X[23]);
    // These are heuristic sanity checks, NOT fully safe pointer validation.
    if (in >= 0x10000 && out >= 0x10000 && (in & 7) == 0 && (out & 7) == 0) {
        // Single producer. PC snapshot reader must re-read generation + count.
        g_mailbox.generation.fetch_add(1, std::memory_order_acq_rel);
        g_mailbox.tick = static_cast<uint64_t>(nn::os::GetSystemTick().GetInt64Value());
        std::memcpy(g_mailbox.source, reinterpret_cast<const void*>(in + SourceRows), RowBytes);
        std::memcpy(g_mailbox.bootstrap, reinterpret_cast<const void*>(out + WireRows), RowBytes);
        g_mailbox.count.fetch_add(1, std::memory_order_relaxed);
        g_mailbox.generation.fetch_add(1, std::memory_order_release);
    } else {
        g_mailbox.dropped.fetch_add(1, std::memory_order_relaxed);
    }
    g_writer.clear(std::memory_order_release);
}
} // namespace rcprobe

HOOK_DEFINE_INLINE(RaidBootstrapReadOnly) {
    static void Callback(exl::hook::InlineCtx* ctx) { rcprobe::Capture(ctx); }
};

extern "C" void exl_main(void*, void*) {
    char info[128]{};
    std::snprintf(info, sizeof(info),
        "[RC-N1] mailbox=0x%016llX protocol=1 size=0x600",
        static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(&rcprobe::g_mailbox)));
    rcprobe::Log(info);
    // The mailbox starts DISABLED even when the hook is installed.
    if (!rcprobe::VerifyGameFingerprint()) {
        rcprobe::g_mailbox.status.store(2, std::memory_order_release);
        rcprobe::Log("[RC-N1] unrecognized game instructions; hook disabled");
        return;
    }
    exl::hook::Initialize();
    RaidBootstrapReadOnly::InstallAtOffset(rcprobe::HookRva);
    rcprobe::g_mailbox.status.store(1, std::memory_order_release);
    rcprobe::Log("[RC-N1] hook installed; PC must enable mailbox capture");
}
extern "C" NORETURN void exl_exception_entry() {
    EXL_ABORT("[RC-N1] exlaunch exception");
}
