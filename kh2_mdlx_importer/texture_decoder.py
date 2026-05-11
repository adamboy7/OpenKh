"""
KH2 ModelTexture decoder.
Ported from:
  OpenKh.Kh2.ModelTexture.cs
  OpenKh.Kh2.Ps2.Reform8.cs / Reform4.cs / Reform32.cs
"""
import struct


# PS2 pixel storage modes (GsPSM)
GS_PSMCT32 = 0   # RGBA32
GS_PSMT8   = 19  # 8-bit indexed
GS_PSMT4   = 20  # 4-bit indexed


class DecodedTexture:
    __slots__ = ("width", "height", "pixels")

    def __init__(self, width, height, pixels):
        self.width = width
        self.height = height
        self.pixels = pixels  # bytes, RGBA8888, width*height*4 bytes


# ---------------------------------------------------------------------------
# PS2 swizzle tables (verbatim from OpenKh.Kh2.Ps2.Reform*.cs)
# ---------------------------------------------------------------------------

_tbl8bc = [
    0, 1, 4, 5, 16, 17, 20, 21, 2, 3, 6, 7, 18, 19, 22, 23,
    8, 9, 12, 13, 24, 25, 28, 29, 10, 11, 14, 15, 26, 27, 30, 31,
]

_tbl8c0 = [
    0x00, 0x04, 0x10, 0x14, 0x20, 0x24, 0x30, 0x34, 0x02, 0x06, 0x12, 0x16, 0x22, 0x26, 0x32, 0x36,
    0x08, 0x0c, 0x18, 0x1c, 0x28, 0x2c, 0x38, 0x3c, 0x0a, 0x0e, 0x1a, 0x1e, 0x2a, 0x2e, 0x3a, 0x3e,
    0x21, 0x25, 0x31, 0x35, 0x01, 0x05, 0x11, 0x15, 0x23, 0x27, 0x33, 0x37, 0x03, 0x07, 0x13, 0x17,
    0x29, 0x2d, 0x39, 0x3d, 0x09, 0x0d, 0x19, 0x1d, 0x2b, 0x2f, 0x3b, 0x3f, 0x0b, 0x0f, 0x1b, 0x1f,
]

_tbl8c1 = [
    0x20, 0x24, 0x30, 0x34, 0x00, 0x04, 0x10, 0x14, 0x22, 0x26, 0x32, 0x36, 0x02, 0x06, 0x12, 0x16,
    0x28, 0x2c, 0x38, 0x3c, 0x08, 0x0c, 0x18, 0x1c, 0x2a, 0x2e, 0x3a, 0x3e, 0x0a, 0x0e, 0x1a, 0x1e,
    0x01, 0x05, 0x11, 0x15, 0x21, 0x25, 0x31, 0x35, 0x03, 0x07, 0x13, 0x17, 0x23, 0x27, 0x33, 0x37,
    0x09, 0x0d, 0x19, 0x1d, 0x29, 0x2d, 0x39, 0x3d, 0x0b, 0x0f, 0x1b, 0x1f, 0x2b, 0x2f, 0x3b, 0x3f,
]

_tbl32bc = [
    0, 1, 4, 5, 16, 17, 20, 21, 2, 3, 6, 7, 18, 19, 22, 23,
    8, 9, 12, 13, 24, 25, 28, 29, 10, 11, 14, 15, 26, 27, 30, 31,
]

_tbl32pao = [0, 1, 4, 5, 8, 9, 12, 13, 2, 3, 6, 7, 10, 11, 14, 15]

_tbl4bc = [
    0, 2, 8, 10, 1, 3, 9, 11, 4, 6, 12, 14, 5, 7, 13, 15,
    16, 18, 24, 26, 17, 19, 25, 27, 20, 22, 28, 30, 21, 23, 29, 31,
]

_tbl4col0 = [
    0x00, 0x20, 0x80, 0xa0, 0x100, 0x120, 0x180, 0x1a0, 0x08, 0x28, 0x88, 0xa8, 0x108, 0x128, 0x188, 0x1a8,
    0x10, 0x30, 0x90, 0xb0, 0x110, 0x130, 0x190, 0x1b0, 0x18, 0x38, 0x98, 0xb8, 0x118, 0x138, 0x198, 0x1b8,
    0x40, 0x60, 0xc0, 0xe0, 0x140, 0x160, 0x1c0, 0x1e0, 0x48, 0x68, 0xc8, 0xe8, 0x148, 0x168, 0x1c8, 0x1e8,
    0x50, 0x70, 0xd0, 0xf0, 0x150, 0x170, 0x1d0, 0x1f0, 0x58, 0x78, 0xd8, 0xf8, 0x158, 0x178, 0x1d8, 0x1f8,
    0x104, 0x124, 0x184, 0x1a4, 0x04, 0x24, 0x84, 0xa4, 0x10c, 0x12c, 0x18c, 0x1ac, 0x0c, 0x2c, 0x8c, 0xac,
    0x114, 0x134, 0x194, 0x1b4, 0x14, 0x34, 0x94, 0xb4, 0x11c, 0x13c, 0x19c, 0x1bc, 0x1c, 0x3c, 0x9c, 0xbc,
    0x144, 0x164, 0x1c4, 0x1e4, 0x44, 0x64, 0xc4, 0xe4, 0x14c, 0x16c, 0x1cc, 0x1ec, 0x4c, 0x6c, 0xcc, 0xec,
    0x154, 0x174, 0x1d4, 0x1f4, 0x54, 0x74, 0xd4, 0xf4, 0x15c, 0x17c, 0x1dc, 0x1fc, 0x5c, 0x7c, 0xdc, 0xfc,
]

_tbl4col1 = [
    0x100, 0x120, 0x180, 0x1a0, 0x00, 0x20, 0x80, 0xa0, 0x108, 0x128, 0x188, 0x1a8, 0x08, 0x28, 0x88, 0xa8,
    0x110, 0x130, 0x190, 0x1b0, 0x10, 0x30, 0x90, 0xb0, 0x118, 0x138, 0x198, 0x1b8, 0x18, 0x38, 0x98, 0xb8,
    0x140, 0x160, 0x1c0, 0x1e0, 0x40, 0x60, 0xc0, 0xe0, 0x148, 0x168, 0x1c8, 0x1e8, 0x48, 0x68, 0xc8, 0xe8,
    0x150, 0x170, 0x1d0, 0x1f0, 0x50, 0x70, 0xd0, 0xf0, 0x158, 0x178, 0x1d8, 0x1f8, 0x58, 0x78, 0xd8, 0xf8,
    0x04, 0x24, 0x84, 0xa4, 0x104, 0x124, 0x184, 0x1a4, 0x0c, 0x2c, 0x8c, 0xac, 0x10c, 0x12c, 0x18c, 0x1ac,
    0x14, 0x34, 0x94, 0xb4, 0x114, 0x134, 0x194, 0x1b4, 0x1c, 0x3c, 0x9c, 0xbc, 0x11c, 0x13c, 0x19c, 0x1bc,
    0x44, 0x64, 0xc4, 0xe4, 0x144, 0x164, 0x1c4, 0x1e4, 0x4c, 0x6c, 0xcc, 0xec, 0x14c, 0x16c, 0x1cc, 0x1ec,
    0x54, 0x74, 0xd4, 0xf4, 0x154, 0x174, 0x1d4, 0x1f4, 0x5c, 0x7c, 0xdc, 0xfc, 0x15c, 0x17c, 0x1dc, 0x1fc,
]


# ---------------------------------------------------------------------------
# PS2 unswizzle helpers (Decode8, Encode32, Decode4, Encode32 for 4-bit)
# ---------------------------------------------------------------------------

def _encode32(src, bw, bh):
    buf = bytearray(len(src))
    for i in range(0, 0x20 * bh, 0x20):
        for j in range(0, 0x40 * bw, 0x40):
            num3 = 0x2000 * ((j // 0x40) + (bw * (i // 0x20)))
            for k in range(0, 0x20, 8):
                for m in range(0, 0x40, 8):
                    num6 = 0x100 * _tbl32bc[(m // 8) + ((k // 8) * 8)]
                    for n in range(4):
                        num8 = 0x40 * n
                        for num9 in range(0x10):
                            num10 = (j + m) + (num9 % 8)
                            num11 = ((i + k) + (2 * n)) + (num9 // 8)
                            idx = 4 * (num10 + (0x40 * bw * num11))
                            num13 = (4 * _tbl32pao[num9] + num8 + num6) + num3
                            buf[num13]     = src[idx]
                            buf[num13 + 1] = src[idx + 1]
                            buf[num13 + 2] = src[idx + 2]
                            buf[num13 + 3] = src[idx + 3]
    return bytes(buf)


def _decode8(src, bw, bh):
    buf = bytearray(len(src))
    for i in range(0, 0x40 * bh, 0x40):
        for j in range(0, 0x80 * bw, 0x80):
            num3 = 0x2000 * ((j // 0x80) + (bw * (i // 0x40)))
            for k in range(0, 0x40, 0x10):
                for m in range(0, 0x80, 0x10):
                    num6 = 0x100 * _tbl8bc[(m // 0x10) + (8 * (k // 0x10))]
                    for n in range(4):
                        num8 = 0x40 * n
                        tbl = _tbl8c0 if (n & 1) == 0 else _tbl8c1
                        for num9 in range(0x40):
                            idx = ((num3 + num6) + num8) + tbl[num9]
                            num11 = (j + m) + (num9 % 0x10)
                            num12 = ((i + k) + (4 * n)) + (num9 // 0x10)
                            num13 = num11 + (0x80 * bw * num12)
                            buf[num13] = src[idx]
    return bytes(buf)


def _decode4(src, bw, bh):
    buf = bytearray(len(src))
    for i in range(0, 0x80 * bh, 0x80):
        for j in range(0, 0x80 * bw, 0x80):
            num3 = 0x2000 * ((j // 0x80) + (bw * (i // 0x80)))
            for k in range(0, 0x80, 0x10):
                for m in range(0, 0x80, 0x20):
                    num6 = 0x100 * _tbl4bc[(m // 0x20) + (4 * (k // 0x10))]
                    for n in range(4):
                        num8 = 0x40 * n
                        col = _tbl4col0 if (n & 1) == 0 else _tbl4col1
                        for num9 in range(0x80):
                            num10 = col[num9] // 8
                            num11 = col[num9] % 8
                            nibble = (src[((num3 + num6) + num8) + num10] >> num11) & 0xF
                            num13 = (j + m) + (num9 % 0x20)
                            num14 = ((i + k) + (4 * n)) + (num9 // 0x20)
                            num15 = num13 + (0x80 * bw * num14)
                            old = buf[num15 // 2]
                            if (num15 & 1) == 1:
                                buf[num15 // 2] = (old & 0xF0) | nibble
                            else:
                                buf[num15 // 2] = (old & 0x0F) | (nibble << 4)
    return bytes(buf)


# ---------------------------------------------------------------------------
# CLUT (palette) helpers
# ---------------------------------------------------------------------------

def get_clut_pointer(index, cbp, csa):
    """Port of ModelTexture.GetClutPointer."""
    return ((index & 7) + (index & 8) * 8 + (index & 16) // 2 + (index & ~31) * 4
            + (cbp & 7) * 0x4 + (cbp & 8) * 0x80 + (cbp & 16) * 0x2 + (cbp & ~31) * 0x40
            + (csa & 1) * 0x8 + (csa & 14) * 0x40)


def _build_clut8(palette, cbp, csa):
    """Decode 256-entry CLUT → bytes (256 × RGBA)."""
    out = bytearray(256 * 4)
    for i in range(256):
        src = get_clut_pointer(i, cbp, csa) * 4
        out[i*4]     = palette[src]
        out[i*4 + 1] = palette[src + 1]
        out[i*4 + 2] = palette[src + 2]
        out[i*4 + 3] = min(palette[src + 3] * 2, 255)  # PS2 alpha → standard
    return bytes(out)


def _build_clut4(palette, cbp, csa):
    """Decode 16-entry CLUT → bytes (16 × RGBA)."""
    out = bytearray(16 * 4)
    for i in range(16):
        src = get_clut_pointer(i, cbp, csa) * 4
        out[i*4]     = palette[src]
        out[i*4 + 1] = palette[src + 1]
        out[i*4 + 2] = palette[src + 2]
        out[i*4 + 3] = min(palette[src + 3] * 2, 255)
    return bytes(out)


def _indexed8_to_rgba(pixel_data, clut):
    """Convert 8-bit indexed pixel data to RGBA using 256-entry clut."""
    out = bytearray(len(pixel_data) * 4)
    for i, idx in enumerate(pixel_data):
        out[i*4]     = clut[idx*4]
        out[i*4 + 1] = clut[idx*4 + 1]
        out[i*4 + 2] = clut[idx*4 + 2]
        out[i*4 + 3] = clut[idx*4 + 3]
    return bytes(out)


def _indexed4_to_rgba(pixel_data, clut):
    """Convert 4-bit packed indexed pixel data to RGBA using 16-entry clut."""
    pixel_count = len(pixel_data) * 2
    out = bytearray(pixel_count * 4)
    for i in range(len(pixel_data)):
        byte = pixel_data[i]
        hi = (byte >> 4) & 0xF
        lo = byte & 0xF
        # Low nibble = first pixel, high nibble = second (PS2 convention)
        for j, idx in enumerate((lo, hi)):
            p = (i * 2 + j) * 4
            out[p]     = clut[idx*4]
            out[p + 1] = clut[idx*4 + 1]
            out[p + 2] = clut[idx*4 + 2]
            out[p + 3] = clut[idx*4 + 3]
    return bytes(out)


def _rgba32_to_rgba(pixel_data):
    """Convert PS2 RGBA32 (BGRA order with PS2 alpha) to standard RGBA."""
    out = bytearray(len(pixel_data))
    for i in range(0, len(pixel_data) - 3, 4):
        out[i]     = pixel_data[i + 2]  # R ← B
        out[i + 1] = pixel_data[i + 1]  # G
        out[i + 2] = pixel_data[i + 0]  # B ← R
        out[i + 3] = min(pixel_data[i + 3] * 2, 255)  # A
    return bytes(out)


# ---------------------------------------------------------------------------
# Bit field helper
# ---------------------------------------------------------------------------

def _get_bits(data, position, size):
    mask = (1 << size) - 1
    return (data >> position) & mask


# ---------------------------------------------------------------------------
# Main texture decoder
# ---------------------------------------------------------------------------

def decode_model_textures(tex_data: bytes) -> list:
    """
    Decode all textures from a BAR type-7 (ModelTexture) entry.
    Returns list of DecodedTexture objects.
    """
    if len(tex_data) < 36:
        return []

    # Header (9 × int32 = 36 bytes)
    hdr = struct.unpack_from("<9i", tex_data, 0)
    magic_code      = hdr[0]
    color_count     = hdr[1]
    tex_info_count  = hdr[2]
    gs_info_count   = hdr[3]
    offset1         = hdr[4]
    texinf1_off     = hdr[5]
    texinf2_off     = hdr[6]
    picture_offset  = hdr[7]
    palette_offset  = hdr[8]

    if magic_code == -1:
        return []

    # OffsetData: gs_info_count bytes mapping gs_index → texture transfer index
    offset_data = tex_data[offset1: offset1 + gs_info_count]

    # Texture transfer structs (0x90 bytes each)
    # First is CLUT transfer; following tex_info_count are pixel transfers
    TRANSFER_SIZE = 0x90
    BITBLTBUF_OFF = 0x20   # offset within struct to BITBLTBUF (8 bytes)
    DATA_OFFSET_OFF = 0x74  # offset within struct to DataOffset (int32)

    clut_transfer_base = texinf1_off
    clut_bitbltbuf = struct.unpack_from("<q", tex_data, clut_transfer_base + BITBLTBUF_OFF)[0]
    palette_base_ptr = _get_bits(clut_bitbltbuf, 32, 14)  # DBP

    tex_transfers = []
    for i in range(tex_info_count):
        base = texinf1_off + TRANSFER_SIZE + i * TRANSFER_SIZE
        bitbltbuf = struct.unpack_from("<q", tex_data, base + BITBLTBUF_OFF)[0]
        upload_psm = _get_bits(bitbltbuf, 56, 6)  # DPSM
        data_offset = struct.unpack_from("<i", tex_data, base + DATA_OFFSET_OFF)[0]
        tex_transfers.append((upload_psm, data_offset))

    # GS info structs (0xA0 bytes each)
    # Tex0 is the 15th field (0-indexed field 14) at byte offset +0x70
    GS_INFO_SIZE = 0xA0
    TEX0_OFF = 0x70  # offset within struct to GsTex Tex0 (8 bytes)
    CLAMP_OFF = 0x80  # offset to CLAMP

    gs_infos = []
    for i in range(gs_info_count):
        base = texinf2_off + i * GS_INFO_SIZE
        tex0 = struct.unpack_from("<q", tex_data, base + TEX0_OFF)[0]
        psm  = _get_bits(tex0, 20, 6)
        tw   = _get_bits(tex0, 26, 4)
        th   = _get_bits(tex0, 30, 4)
        cbp  = _get_bits(tex0, 37, 14)
        csa  = _get_bits(tex0, 56, 5)
        gs_infos.append((psm, tw, th, cbp, csa))

    # Palette data
    palette_size = color_count * 4
    palette_data = tex_data[palette_offset: palette_offset + palette_size]

    # Decode each GS info entry into a DecodedTexture
    textures = []
    for i in range(gs_info_count):
        if i >= len(offset_data):
            break
        ti = offset_data[i]
        if ti >= len(tex_transfers):
            continue
        upload_psm, data_offset = tex_transfers[ti]
        psm, tw, th, cbp, csa = gs_infos[i]

        width  = 1 << tw
        height = 1 << th

        try:
            if psm == GS_PSMT8:
                data_len = width * height
                raw = tex_data[data_offset: data_offset + data_len]
                if upload_psm == GS_PSMCT32:
                    # Unswizzle: encode32 → decode8
                    bw = max(width // 128, 1)
                    bh = max(height // 64, 1)
                    padded = raw.ljust(bw * bh * 0x2000, b"\x00")
                    raw = _decode8(_encode32(padded, bw, bh), bw, bh)[:data_len]
                clut = _build_clut8(palette_data, cbp - palette_base_ptr, csa)
                pixels = _indexed8_to_rgba(raw, clut)

            elif psm == GS_PSMT4:
                data_len = (width * height) // 2
                raw = tex_data[data_offset: data_offset + data_len]
                if upload_psm == GS_PSMCT32:
                    bw = max(width // 128, 1)
                    bh = max(height // 128, 1)
                    padded = raw.ljust(bw * bh * 0x2000, b"\x00")
                    raw = _decode4(_encode32(padded, bw, bh), bw, bh)[:data_len]
                clut = _build_clut4(palette_data, cbp - palette_base_ptr, csa)
                pixels = _indexed4_to_rgba(raw, clut)

            else:
                # RGBA32 or other direct format
                data_len = width * height * 4
                raw = tex_data[data_offset: data_offset + data_len]
                pixels = _rgba32_to_rgba(raw)

            textures.append(DecodedTexture(width, height, pixels))

        except Exception as e:
            # Append a placeholder pink texture on decode failure
            pixels = bytes([255, 0, 255, 255] * (width * height))
            textures.append(DecodedTexture(width, height, pixels))

    return textures
