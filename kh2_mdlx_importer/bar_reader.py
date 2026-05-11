import struct


BAR_MAGIC = 0x01524142  # 'BAR\x01' little-endian


class BarEntry:
    __slots__ = ("type", "index", "name", "data")

    def __init__(self, type_id, index, name, data):
        self.type = type_id
        self.index = index
        self.name = name
        self.data = data


def read_bar(data: bytes) -> list:
    if len(data) < 16:
        raise ValueError("Data too short for BAR header")

    magic, entry_count = struct.unpack_from("<II", data, 0)
    if magic != BAR_MAGIC:
        raise ValueError(f"Not a BAR file (magic={magic:#010x})")

    entries = []
    for i in range(entry_count):
        entry_offset = 16 + i * 16
        type_id, index = struct.unpack_from("<Hh", data, entry_offset)
        name_bytes = data[entry_offset + 4: entry_offset + 8]
        name = name_bytes.rstrip(b"\x00").decode("ascii", errors="replace")
        offset, size = struct.unpack_from("<ii", data, entry_offset + 8)

        if offset == -1:
            entry_data = b""
        else:
            entry_data = data[offset: offset + size]

        entries.append(BarEntry(type_id, index, name, entry_data))

    return entries


def get_entries_by_type(entries: list, type_id: int) -> list:
    return [e for e in entries if e.type == type_id]
