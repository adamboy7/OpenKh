"""
PS2 VIF1 microprogram emulator.
Ported from OpenKh.Ps2.VifUnpacker.cs
"""
import struct

_CMD_NOP    = 0x00
_CMD_STCYCL = 0x01
_CMD_MSCAL  = 0x14
_CMD_MSCNT  = 0x17
_CMD_STMASK = 0x20
_CMD_STROW  = 0x30
_CMD_STCOL  = 0x31
_CMD_UNPACK_MARKER = 0x60  # bits[7:6] of 7-bit cmd == 0b11

_MASK_WRITE = 0
_MASK_ROW   = 1
_MASK_COL   = 2
_MASK_SKIP  = 3

_VU_MEM_SIZE = 16 * 1024


class VifUnpacker:
    def __init__(self, code: bytes):
        self._code = code
        self._mem = bytearray(_VU_MEM_SIZE)
        self._pc = 0
        self._dest = 0
        self.vif1_tops = 0

        self._cycle_cl = 0
        self._cycle_wl = 0
        self._mask = [0, 0, 0, 0]   # 4 bytes, 2-bit MaskType per component
        self._row = [0, 0, 0, 0]
        self._col = [0, 0, 0, 0]
        self._enable_mask = False
        self._mask_index = 0

    @property
    def memory(self) -> bytes:
        return bytes(self._mem)

    def run(self) -> str:
        """Run VIF until end of code or MSCAL/MSCNT. Returns 'end' or 'microprogram'."""
        while True:
            state = self._step()
            if state != "run":
                return state

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    def _read_uint32(self) -> int:
        v = struct.unpack_from("<I", self._code, self._pc)[0]
        self._pc += 4
        return v

    def _read_int8(self) -> int:
        v = struct.unpack_from("b", self._code, self._pc)[0]
        self._pc += 1
        return v & 0xFFFFFFFF

    def _read_uint8(self) -> int:
        v = self._code[self._pc]
        self._pc += 1
        return v

    def _read_int16(self) -> int:
        v = struct.unpack_from("<h", self._code, self._pc)[0]
        self._pc += 2
        return v & 0xFFFFFFFF

    def _read_uint16(self) -> int:
        v = struct.unpack_from("<H", self._code, self._pc)[0]
        self._pc += 2
        return v

    def _write(self, value: int):
        struct.pack_into("<I", self._mem, self._dest, value & 0xFFFFFFFF)

    def _next_dest(self):
        self._dest += 4

    def _next_mask(self) -> list:
        if not self._enable_mask:
            return [_MASK_WRITE, _MASK_WRITE, _MASK_WRITE, _MASK_WRITE]
        byte = self._mask[self._mask_index & 3]
        self._mask_index += 1
        return [(byte >> (i * 2)) & 3 for i in range(4)]

    # ------------------------------------------------------------------
    # Unpack component handlers (VN dimension)
    # ------------------------------------------------------------------

    def _unpack_single(self, reader):
        mask = self._next_mask()
        value = reader()
        # Broadcast same value to all 4 components
        # Next() is ALWAYS called regardless of mask (matches C# behaviour)
        if mask[0] == _MASK_WRITE: self._write(value)
        self._next_dest()
        if mask[1] == _MASK_WRITE: self._write(value)
        self._next_dest()
        if mask[2] == _MASK_WRITE: self._write(value)
        self._next_dest()
        if mask[3] == _MASK_WRITE: self._write(value)
        self._next_dest()

    def _unpack_v2(self, reader):
        mask = self._next_mask()
        x = reader()
        y = reader()
        # Z = X, W = Y (PCSX2 undefined-behaviour emulation)
        if mask[0] == _MASK_WRITE: self._write(x)
        self._next_dest()
        if mask[1] == _MASK_WRITE: self._write(y)
        self._next_dest()
        if mask[2] == _MASK_WRITE: self._write(x)
        self._next_dest()
        if mask[3] == _MASK_WRITE: self._write(y)
        self._next_dest()

    def _unpack_v3(self, reader):
        mask = self._next_mask()
        if mask[0] == _MASK_WRITE: self._write(reader())
        self._next_dest()
        if mask[1] == _MASK_WRITE: self._write(reader())
        self._next_dest()
        if mask[2] == _MASK_WRITE: self._write(reader())
        self._next_dest()
        # W: read the next value but do NOT advance PC (PCSX2 hardware quirk)
        if mask[3] == _MASK_WRITE:
            saved_pc = self._pc
            self._write(reader())
            self._pc = saved_pc
        self._next_dest()

    def _unpack_v4(self, reader):
        mask = self._next_mask()
        if mask[0] == _MASK_WRITE: self._write(reader())
        self._next_dest()
        if mask[1] == _MASK_WRITE: self._write(reader())
        self._next_dest()
        if mask[2] == _MASK_WRITE: self._write(reader())
        self._next_dest()
        if mask[3] == _MASK_WRITE: self._write(reader())
        self._next_dest()

    # ------------------------------------------------------------------
    # UNPACK opcode handler
    # ------------------------------------------------------------------

    def _do_unpack(self, opcode_word: int):
        # Destination address (QW units) — TOPS always added (C# has commented-out guard)
        addr = opcode_word & 0x1FF
        self._dest = (addr + self.vif1_tops) * 16

        num  = (opcode_word >> 16) & 0xFF           # number of vectors
        vl   = (opcode_word >> 24) & 0x3            # data width code
        vn   = (opcode_word >> 26) & 0x3            # vector dimension code
        mask_enable  = (opcode_word >> 28) & 0x1
        # Bit10==0 means unsigned (C# naming is inverted vs PS2 spec, match exactly)
        is_unsigned = (opcode_word & 0x400) == 0

        self._enable_mask = bool(mask_enable)
        self._mask_index = 0

        # Select reader based on data width
        if is_unsigned:
            _readers = [self._read_uint32, self._read_uint16, self._read_uint8, self._read_uint16]
        else:
            _readers = [self._read_uint32, self._read_int16, self._read_int8, self._read_int16]
        reader = _readers[vl]

        # Select unpacker based on vector dimension
        _unpackers = [self._unpack_single, self._unpack_v2, self._unpack_v3, self._unpack_v4]
        unpacker = _unpackers[vn]

        for _ in range(num):
            unpacker(reader)

        # Align PC to 4-byte boundary after UNPACK data
        self._pc = (self._pc + 3) & ~3

    # ------------------------------------------------------------------
    # Step / Run
    # ------------------------------------------------------------------

    def _step(self) -> str:
        if self._pc >= len(self._code):
            return "end"

        opcode_word = self._read_uint32()
        cmd_bits = (opcode_word >> 24) & 0x7F

        # UNPACK: top 2 bits of 7-bit cmd == 0b11
        if (cmd_bits & 0x60) == 0x60:
            self._do_unpack(opcode_word)
        elif cmd_bits == _CMD_NOP:
            pass
        elif cmd_bits == _CMD_STCYCL:
            imm = opcode_word & 0xFFFF
            self._cycle_cl = imm & 0xFF
            self._cycle_wl = (imm >> 8) & 0xFF
        elif cmd_bits == _CMD_MSCAL or cmd_bits == _CMD_MSCNT:
            return "microprogram"
        elif cmd_bits == _CMD_STMASK:
            mask_word = self._read_uint32()
            self._mask = [(mask_word >> (i * 8)) & 0xFF for i in range(4)]
        elif cmd_bits == _CMD_STROW:
            self._row = [self._read_uint32() for _ in range(4)]
        elif cmd_bits == _CMD_STCOL:
            self._col = [self._read_uint32() for _ in range(4)]
        else:
            raise ValueError(f"Unknown VIF1 cmd 0x{cmd_bits:02X} at pc={self._pc - 4:#x}")

        return "run"
