// Violet 4.0.0 offline-only proof-of-layout: no device writes in this file.
// Source point: main+0xE32984. x23 contains 0xAA0 plaintext after memcpy.
// At runtime, segment-padding mapping/executability MUST be checked separately.
.text
.global native_stub
native_stub:
    adr x9, telemetry
    ldr w10, [x9, #4]
    add w10, w10, #1
    str w10, [x9, #4]
    ldr w10, [x9]
    cbz w10, skip
    ldr x10, [x23, #0x6b8]
    movz x11, #0x6152
    movk x11, #0x6469, lsl #16
    movk x11, #0x6f50, lsl #32
    movk x11, #0x6e69, lsl #48
    cmp x10, x11
    b.ne rejected
    ldrh w10, [x23, #0x6c0]
    movz w11, #0x5f74
    cmp w10, w11
    b.ne rejected
    stp x29, x30, [sp, #-16]!
    add x0, x23, #0x79c
    adr x1, rewards_data
    movz w2, #0x2d0
bl_memcpy_here:
    .word 0  // BL memcpy@plt: builder must relocate
    ldp x29, x30, [sp], #16
    adr x9, telemetry
    ldr w10, [x9, #8]
    add w10, w10, #1
    str w10, [x9, #8]
    b skip
rejected:
    ldr w10, [x9, #12]
    add w10, w10, #1
    str w10, [x9, #12]
skip:
    mov x8, x21  // preserve original overwritten instruction
b_back_here:
    .word 0  // B main+0xE32988: builder must relocate
    .org 0x90
telemetry:
    .word 0  // mode: 0 observe, 1 experimental apply
    .word 0  // observed serializer calls
    .word 0  // copies (not inventory gain)
    .word 0  // rejected magic/shape
    .org 0xa0
rewards_data:
    .space 720, 0
