// RaidCrawler N1 native-side, gameplay-data READ-ONLY probe.
// Uses exlaunch inline instrumentation; the framework patches one CODE site.
// No reward/packet/save object is written or modified by our callback.
//
// This file is an overlay for shadowninja108/exlaunch (pinned in build.sh).
// Only Violet 4.0.0 is fingerprinted. Do not deploy against newer builds.
#include "lib.hpp"
#include <nn/fs.hpp>
#include <nn/os.hpp>

#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace rcprobe {

constexpr uintptr_t kHookRva = 0xE32984; // immediately AFTER memcpy@plt returns
constexpr size_t kRowRegionLength = 45 * 16;
constexpr size_t kSourceRowsOffset = 0x58 + 0xE4; // x20 input object + 0x13c
constexpr size_t kPlainRowsOffset = 0x6B8 + 0xE4; // x23 output buffer + 0x79c
constexpr size_t kBufferSlots = 8;
static_assert(kSourceRowsOffset == 0x13C);
static_assert(kPlainRowsOffset == 0x79C);
static_assert(0x79C + kRowRegionLength == 0xA6C);
static_assert(0xA6C < 0xAA0);

struct OpcodeGuard {
    uintptr_t rva;
    uint32_t opcode;
};
// Recorded from the SHA-256-audited Violet 4.0.0 ELF. These are only
// version fingerprints, NOT an automatically retrieved NSO Build ID.
constexpr OpcodeGuard kOpCodes[] = {
    {0xE327FC, 0xA9BB7BFD}, {0xE32814, 0xAA0003F4},
    {0xE32828, 0x52815416}, {0xE3283C, 0xAA0003F7},
    {0xE32844, 0x52802B18}, {0xE32960, 0x911B62E0},
    {0xE32964, 0xA945AA8B}, {0xE32968, 0x9101E281},
    {0xE3296C, 0x52807902}, {0xE32970, 0xF9035EEB},
    {0xE32980, 0x94982624}, {0xE329B0, 0x141D8073},
};

static bool HasExpectedFingerprint() {
    const uintptr_t start = exl::util::modules::GetTargetStart();
    for (const auto& guard : kOpCodes) {
        auto* word = reinterpret_cast<volatile const uint32_t*>(start + guard.rva);
        if (*word != guard.opcode)
            return false;
    }
    return true;
}

static void Debug(const char* msg) {
    svcOutputDebugString(msg, std::strlen(msg));
}

enum class SlotState : unsigned {
    Free, Filling, Ready, Saving,
};
struct Snapshot {
    std::atomic<unsigned> state{static_cast<unsigned>(SlotState::Free)};
    uint64_t serial = 0;
    uint64_t tick = 0;
    uint8_t sourceRows[kRowRegionLength]{};
    uint8_t bootstrapRows[kRowRegionLength]{};
};
static Snapshot g_slots[kBufferSlots];
static std::atomic_flag g_producerLock = ATOMIC_FLAG_INIT;
static std::atomic<bool> g_storageReady{false};
static std::atomic<bool> g_captureEnabled{false};
static uint64_t g_nextSerial = 0;
static uint64_t g_bootTick = 0;
static nn::os::ThreadType g_worker;
alignas(0x1000) static uint8_t g_threadStack[0x8000];

static bool WriteExact(const char* path, const void* bytes, size_t length) {
    const Result created = nn::fs::CreateFile(path, static_cast<s64>(length));
    if (created != 0)
        return false; // never overwrite an earlier run's capture
    nn::fs::FileHandle file{};
    if (nn::fs::OpenFile(&file, path, nn::fs::OpenMode_Write) != 0)
        return false;
    const bool okay = nn::fs::WriteFile(
        file, 0, bytes, length,
        nn::fs::WriteOption::CreateOption(nn::fs::WriteOptionFlag_Flush)) == 0;
    nn::fs::CloseFile(file);
    return okay;
}

static bool EnabledBySdSentinel() {
    nn::fs::FileHandle sentinel{};
    if (nn::fs::OpenFile(
        &sentinel, "rcprobe:/raidcrawler/host-probe.enable",
        nn::fs::OpenMode_Read) != 0)
        return false;
    nn::fs::CloseFile(sentinel);
    return true;
}

static void PersistSnapshot(const Snapshot& slot) {
    char source[160]{};
    char destination[160]{};
    char metadata[160]{};
    const unsigned long long run = static_cast<unsigned long long>(g_bootTick);
    const unsigned long long seq = static_cast<unsigned long long>(slot.serial);
    std::snprintf(source, sizeof(source),
        "rcprobe:/raidcrawler/n1/%016llx_%05llu.source-rewards.bin", run, seq);
    std::snprintf(destination, sizeof(destination),
        "rcprobe:/raidcrawler/n1/%016llx_%05llu.bootstrap-rewards.bin", run, seq);
    std::snprintf(metadata, sizeof(metadata),
        "rcprobe:/raidcrawler/n1/%016llx_%05llu.meta.txt", run, seq);

    // The two raw files are EXACTLY 720 bytes each. No Pokémon, user identity,
    // session secrets, or entire message buffers are persisted.
    const bool srcWritten = WriteExact(source, slot.sourceRows, kRowRegionLength);
    const bool dstWritten = WriteExact(destination, slot.bootstrapRows, kRowRegionLength);
    if (!srcWritten || !dstWritten) {
        Debug("[RC-N1] Capture output failed. Check SD access and free space.");
        return;
    }
    char info[320]{};
    const int n = std::snprintf(info, sizeof(info),
        "format=rcprobe-v1\n"
        "game=Violet\n"
        "version_fingerprint=4.0.0-12-opcodes\n"
        "build_id_verified=NO\n"
        "source_offset=x20+0x13c\n"
        "bootstrap_offset=x23+0x79c\n"
        "bytes_per_region=720\n"
        "boot_tick=%016llx\n"
        "capture_tick=%016llx\n"
        "ordinal=%llu\n"
        "session_provenance=UNVERIFIED\n",
        run, static_cast<unsigned long long>(slot.tick), seq);
    if (n > 0 && n < static_cast<int>(sizeof(info))) {
        (void)WriteExact(metadata, info, static_cast<size_t>(n));
    }
}

static void Worker(void*) {
    // On a production retail title, the Debug SD mount may not be permitted.
    // Fail CLOSED: if not mounted the hook will be installed but inactive.
    if (nn::fs::MountSdCardForDebug("rcprobe") != 0) {
        Debug("[RC-N1] SD mount unavailable; data capture disabled.");
        return;
    }
    (void)nn::fs::CreateDirectory("rcprobe:/raidcrawler");
    (void)nn::fs::CreateDirectory("rcprobe:/raidcrawler/n1");
    g_bootTick = static_cast<uint64_t>(nn::os::GetSystemTick().GetInt64Value());
    g_storageReady.store(true, std::memory_order_release);
    Debug("[RC-N1] Probe ready. Create SD:/raidcrawler/host-probe.enable to opt in.");

    unsigned loopCount = 0;
    for (;;) {
        if (++loopCount >= 20) {
            loopCount = 0;
            g_captureEnabled.store(EnabledBySdSentinel(), std::memory_order_release);
        }
        for (auto& slot : g_slots) {
            unsigned expected = static_cast<unsigned>(SlotState::Ready);
            if (!slot.state.compare_exchange_strong(
                expected, static_cast<unsigned>(SlotState::Saving),
                std::memory_order_acquire))
                continue;
            PersistSnapshot(slot);
            slot.state.store(static_cast<unsigned>(SlotState::Free),
                             std::memory_order_release);
        }
        nn::os::SleepThread(nn::TimeSpan::FromMilliSeconds(100));
    }
}

// Executed synchronously at main+0xE32984. The original memcpy has returned;
// x20=host serialization input object; x23=allocated 0xAA0 plaintext output.
// exlaunch's inline callback preserves guest registers and resumes instruction.
static void OnBootstrapReady(exl::hook::InlineCtx* ctx) {
    if (!g_storageReady.load(std::memory_order_acquire) ||
        !g_captureEnabled.load(std::memory_order_acquire))
        return;
    if (g_producerLock.test_and_set(std::memory_order_acquire))
        return;

    const uintptr_t input = static_cast<uintptr_t>(ctx->X[20]);
    const uintptr_t output = static_cast<uintptr_t>(ctx->X[23]);
    // This checks for clearly bogus values only. Lifetime/type correctness
    // still requires on-device validation on the exact executable build.
    if (input >= 0x10000 && output >= 0x10000 &&
        (input & 7) == 0 && (output & 7) == 0) {
        for (auto& slot : g_slots) {
            unsigned expected = static_cast<unsigned>(SlotState::Free);
            if (!slot.state.compare_exchange_strong(
                    expected, static_cast<unsigned>(SlotState::Filling),
                    std::memory_order_acquire))
                continue;

            // Never mutate the game objects (the two source pointers are const).
            const auto* src = reinterpret_cast<const uint8_t*>(input + kSourceRowsOffset);
            const auto* dst = reinterpret_cast<const uint8_t*>(output + kPlainRowsOffset);
            slot.serial = ++g_nextSerial;
            slot.tick = static_cast<uint64_t>(nn::os::GetSystemTick().GetInt64Value());
            std::memcpy(slot.sourceRows, src, kRowRegionLength);
            std::memcpy(slot.bootstrapRows, dst, kRowRegionLength);
            slot.state.store(static_cast<unsigned>(SlotState::Ready),
                             std::memory_order_release);
            break;
        }
    }
    g_producerLock.clear(std::memory_order_release);
}

} // namespace rcprobe

HOOK_DEFINE_INLINE(RaidBootstrapReadOnly) {
    static void Callback(exl::hook::InlineCtx* ctx) {
        rcprobe::OnBootstrapReady(ctx);
    }
};

extern "C" void exl_main(void*, void*) {
    // The compile-time PROGRAM_ID and all 12 instruction guards limit the
    // target. Real Build ID MUST also be independently checked on console.
    if (!rcprobe::HasExpectedFingerprint()) {
        svcOutputDebugString("[RC-N1] 4.0.0 fingerprint mismatch, hook not installed.", 56);
        return;
    }
    exl::hook::Initialize();
    if (nn::os::CreateThread(
            &rcprobe::g_worker, rcprobe::Worker, nullptr,
            rcprobe::g_threadStack, sizeof(rcprobe::g_threadStack),
            nn::os::DefaultThreadPriority + 2, nn::os::IdealCoreUseDefault) != 0) {
        svcOutputDebugString("[RC-N1] Worker init failed; hook not installed.", 47);
        return;
    }
    nn::os::StartThread(&rcprobe::g_worker);
    RaidBootstrapReadOnly::InstallAtOffset(rcprobe::kHookRva);
    svcOutputDebugString("[RC-N1] Instrumentation active; rewards are never altered.", 60);
}

extern "C" NORETURN void exl_exception_entry() {
    EXL_ABORT("[RC-N1] exlaunch exception");
}
