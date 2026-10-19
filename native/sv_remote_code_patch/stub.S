// Violet 4.0.0 offline ARM64 seqlock experiment; no Switch-side plugin.
.text
.global native_stub
native_stub:
    stp x9, x10, [sp, #-16]!
    stp x11, x12, [sp, #-16]!
    stp x13, x14, [sp, #-16]!
    adr x9, epoch
    ldar w14, [x9]
    tbnz w14, #0, done
    ldr x10, [x23, #0x6b8]
    movz x13, #0x6152
    movk x13, #0x6469, lsl #16
    movk x13, #0x6f50, lsl #32
    movk x13, #0x6e69, lsl #48
    cmp x10, x13
    b.ne done
    adr x11, rewards_data
    add x12, x23, #0x79c
    movz w13, #45
copy_loop:
    ldp x9, x10, [x11], #16
    stp x9, x10, [x12], #16
    subs w13, w13, #1
    b.ne copy_loop
    adr x9, epoch
    ldar w10, [x9]
    cmp w10, w14
    b.eq done
    add x11, x20, #0x13c
    add x12, x23, #0x79c
    movz w13, #45
restore_loop:
    ldp x9, x10, [x11], #16
    stp x9, x10, [x12], #16
    subs w13, w13, #1
    b.ne restore_loop
done:
    ldp x13, x14, [sp], #16
    ldp x11, x12, [sp], #16
    ldp x9, x10, [sp], #16
    mov x8, x21
return_branch:
    .word 0
    .org 0x90
epoch:
    .word 1
    .word 0x32534852
    .ascii "RaidPoin"
    .org 0xa0
rewards_data:
    .space 720, 0
