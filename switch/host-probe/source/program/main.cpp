// RaidCrawler host N1/N2; game data read-only by default.
// N2 optional write path is compile-time gated, and NOT present in default Windows artifact.
// Current retail row convention: [item, quantity, rare, subject] at +0xE4.
// Only audited Violet 4.0.0. Hook installation does patch executable code.
#include "lib.hpp"
#include <nn/os.hpp>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace rcprobe {
constexpr uintptr_t HookRva = 0xE32984;
constexpr size_t RowBytes = 45 * 16;
constexpr size_t SourceRows = 0x58 + 0xE4; // row0 at x20 + 0x13C
constexpr size_t WireRows = 0x6B8 + 0xE4; // row0 at x23 + 0x79C
constexpr uint32_t ProtocolVersion = 2;
constexpr uint32_t MailboxBytes = 0x600;
static_assert(SourceRows == 0x13C && WireRows == 0x79C);
static_assert(0x79C + RowBytes == 0xA6C);

struct Mailbox {
    char magic[8] = {'R','C','H','M','B','0','2','\0'};
    uint32_t version = ProtocolVersion;
    uint32_t length = MailboxBytes;
    std::atomic<uint32_t> enabled{0};          // +0x10
    std::atomic<uint32_t> status{0};           // +0x14
    std::atomic<uint32_t> count{0};            // +0x18
    std::atomic<uint32_t> dropped{0};          // +0x1c
    std::atomic<uint32_t> generation{0};       // +0x20, odd = mutating snapshot
    uint32_t capabilities = 0;                // +0x24: 1 if N2 writer compiled
    uint64_t tick = 0;                        // +0x28
    uint64_t session = 0;                     // +0x30 reserved
    uint64_t metadata = 0;                    // +0x38 reserved
    uint8_t source[RowBytes]{};                // +0x40: original 720 B
    uint8_t bootstrap[RowBytes]{};             // +0x310: outgoing 720 B
    std::atomic<uint32_t> requestMode{0};      // +0x5e0: 0 off, 1 preview, 2 append
    uint32_t requestItemId = 0;               // +0x5e4
    uint32_t requestQuantity = 0;             // +0x5e8
    uint32_t requestRareFlag = 0;             // +0x5ec
    uint32_t requestSubject = 0;              // +0x5f0
    std::atomic<uint32_t> lastResult{0};       // +0x5f4
    std::atomic<uint32_t> appendedCount{0};    // +0x5f8
    uint32_t originalRows = 0;                // +0x5fc
};
static_assert(offsetof(Mailbox, enabled) == 0x10);
static_assert(offsetof(Mailbox, status) == 0x14);
static_assert(offsetof(Mailbox, count) == 0x18);
static_assert(offsetof(Mailbox, generation) == 0x20);
static_assert(offsetof(Mailbox, source) == 0x40);
static_assert(offsetof(Mailbox, bootstrap) == 0x310);
static_assert(offsetof(Mailbox, requestMode) == 0x5e0);
static_assert(offsetof(Mailbox, lastResult) == 0x5f4);
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
    for (const auto& op : Fingerprint)
        if (*reinterpret_cast<const volatile uint32_t*>(start+op.rva) != op.word)
            return false;
    return true;
}
static void Log(const char* msg) { svcOutputDebugString(msg, std::strlen(msg)); }

// Return codes: 1=original match; 10=preview accepted; 11=capacity full;
// 12=ambiguous original rows; 13=invalid request; 14=source/dest mismatch;
// 15=write build disabled; 20=single outgoing row appended.
static uint32_t EvaluateAndMaybeAppend(uint8_t* outgoing, uint32_t mode) {
    if (std::memcmp(g_mailbox.source, g_mailbox.bootstrap, RowBytes) != 0)
        return 14;
    if (mode == 0) return 1;
    int used = 0;
    bool paddingSeen = false;
    for (int i = 0; i < 45; i++) {
        uint32_t row[4]{};
        std::memcpy(row, g_mailbox.source + i*16, sizeof(row));
        bool empty = row[0] == 0 && row[1] == 0 && row[2] == 0 && row[3] == 0;
        if (empty) { paddingSeen = true; continue; }
        if (paddingSeen || row[0] == 0 || row[1] == 0 || row[2] > 1 || row[3] > 5)
            return 12;
        used++;
    }
    g_mailbox.originalRows = static_cast<uint32_t>(used);
    if (used >= 45) return 11;
    // Constrained proof-of-concept: only Quick Ball (15) x1..10.
    // This cannot establish arbitrary bag SlotMaxNum or 20x999 validity.
    uint32_t item = g_mailbox.requestItemId;
    uint32_t quantity = g_mailbox.requestQuantity;
    uint32_t rare = g_mailbox.requestRareFlag;
    uint32_t subject = g_mailbox.requestSubject;
    if (g_mailbox.requestMode.load(std::memory_order_acquire) != mode ||
        item != 15 || quantity == 0 || quantity > 10 || rare != 0 || subject != 5)
        return 13;
    if (mode == 1) return 10;
    if (mode != 2) return 13;

#if defined(RC_N2_ENABLE_WRITE) && RC_N2_ENABLE_WRITE == 1
    uint32_t extra[4] = {item, quantity, rare, subject};
    // Outgoing plaintext ONLY: x23+0x79C, before original compression.
    // Never modify the source RaidPoint, existing reward rows, or LZ4 bytes.
    std::memcpy(outgoing + used*16, extra, sizeof(extra));
    std::memcpy(g_mailbox.bootstrap + used*16, extra, sizeof(extra));
    g_mailbox.appendedCount.fetch_add(1, std::memory_order_relaxed);
    return 20;
#else
    return 15;
#endif
}
static void Capture(exl::hook::InlineCtx* ctx) {
    if (g_mailbox.status.load(std::memory_order_acquire) != 1 ||
        g_mailbox.enabled.load(std::memory_order_acquire) != 1)
        return;
    if (g_writer.test_and_set(std::memory_order_acquire)) {
        g_mailbox.dropped.fetch_add(1, std::memory_order_relaxed);
        return;
    }
    const uintptr_t in = static_cast<uintptr_t>(ctx->X[20]);
    const uintptr_t out = static_cast<uintptr_t>(ctx->X[23]);
    if (in >= 0x10000 && out >= 0x10000 && (in & 7) == 0 && (out & 7) == 0) {
        g_mailbox.generation.fetch_add(1, std::memory_order_acq_rel);
        g_mailbox.tick = static_cast<uint64_t>(nn::os::GetSystemTick().GetInt64Value());
        std::memcpy(g_mailbox.source, reinterpret_cast<const void*>(in + SourceRows), RowBytes);
        std::memcpy(g_mailbox.bootstrap, reinterpret_cast<const void*>(out + WireRows), RowBytes);
        // Whole RaidPoint already copied by original memcpy; reject a stale or partial copy.
        bool entirePointMatches = std::memcmp(
            reinterpret_cast<const void*>(in + 0x58),
            reinterpret_cast<const void*>(out + 0x6B8), 0x3E8) == 0;
        uint32_t mode = g_mailbox.requestMode.load(std::memory_order_acquire);
        uint32_t result = entirePointMatches
            ? EvaluateAndMaybeAppend(reinterpret_cast<uint8_t*>(out + WireRows), mode)
            : 14;
        // A write request is ALWAYS one-shot, including rejects and version/
        // capacity mismatches: never let an old request affect a future raid.
        if (mode == 2)
            g_mailbox.requestMode.store(0, std::memory_order_release);
        g_mailbox.lastResult.store(result, std::memory_order_relaxed);
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
        "[RC-N1] mailbox=0x%016llX protocol=2 size=0x600",
        static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(&rcprobe::g_mailbox)));
    rcprobe::Log(info);
    if (!rcprobe::VerifyGameFingerprint()) {
        rcprobe::g_mailbox.status.store(2, std::memory_order_release);
        rcprobe::Log("[RC-N1] unrecognized game instructions; hook disabled");
        return;
    }
#if defined(RC_N2_ENABLE_WRITE) && RC_N2_ENABLE_WRITE == 1
    rcprobe::g_mailbox.capabilities = 1;
#endif
    exl::hook::Initialize();
    RaidBootstrapReadOnly::InstallAtOffset(rcprobe::HookRva);
    rcprobe::g_mailbox.status.store(1, std::memory_order_release);
    rcprobe::Log("[RC-N1] hook installed; PC must enable mailbox capture");
}
extern "C" NORETURN void exl_exception_entry() {
    EXL_ABORT("[RC-N1] exlaunch exception");
}
