// Violet 4.0.0 OFFLINE ONLY. The RX text page cannot be written by game CPU.
// Never place game-written hit counters in the code cave; no writable scratch exists yet.
.text
.global native_stub
native_stub:
    adr x9, mode
    ldr w10, [x9]
    cbz w10, skip
    ldr x10, [x23, #0x6b8]
    movz x11, #0x6152
    movk x11, #0x6469, lsl #16
    movk x11, #0x6f50, lsl #32
    movk x11, #0x6e69, lsl #48
    cmp x10, x11
    b.ne skip
    ldrh w10, [x23, #0x6c0]
    movz w11, #0x5f74
    cmp w10, w11
    b.ne skip
    stp x29, x30, [sp, #-16]!
    add x0, x23, #0x79c
    adr x1, rewards_data
    movz w2, #0x2d0
bl_memcpy_here:
    .word 0  // BL memcpy@plt, relocated by offline builder
    ldp x29, x30, [sp], #16
skip:
    mov x8, x21  // original overwritten instruction
b_back_here:
    .word 0  // B main+0xE32988, relocated by offline builder
    .org 0x90
mode:
    .word 0  // RX code-page flag: read by game, changed ONLY through debugger
    .space 12, 0
    .org 0xa0
rewards_data:
    .space 720, 0
