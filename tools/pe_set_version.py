#!/usr/bin/env python3
"""
为交叉编译（Linux）产出的 rcl.exe 注入 VS_VERSION_INFO 版本资源，
使右键“属性 → 详细信息”中出现产品名、版权（Copyright © Rainclouds）等字段。
由于在非 Windows 上 dotnet 无法自定义 apphost 版本资源（NETSDK1074），
这里用 pefile 重建 .rsrc 段，保留原有 RT_MANIFEST，仅替换 RT_VERSION。

用法：
    python3 tools/pe_set_version.py <exe路径> [--company X] [--copyright Y] ...
"""
import struct
import sys
import pefile


def pad4(b):
    while len(b) % 4:
        b += b"\x00"
    return b


def version_info(filever, prodver, copyright, product, company, file_desc):
    strings = [
        ("CompanyName", company),
        ("FileDescription", file_desc),
        ("FileVersion", "%d.%d.%d.%d" % filever),
        ("InternalName", "rcl"),
        ("LegalCopyright", copyright),
        ("OriginalFilename", "rcl.exe"),
        ("ProductName", product),
        ("ProductVersion", "%d.%d.%d.%d" % prodver),
    ]

    entries = b""
    for k, v in strings:
        ks = k.encode("utf-16-le") + b"\x00\x00"
        ks = pad4(ks)
        vs = v.encode("utf-16-le") + b"\x00\x00"
        w_val_len = len(v) + 1
        w_len = 2 + 2 + 2 + len(ks) + len(vs)
        entries += struct.pack("<HHH", w_len, w_val_len, 1) + ks + vs

    lang = struct.pack("<HH", 0x0409, 1252)
    st = pad4(lang + entries)

    sfi_key = pad4("StringFileInfo".encode("utf-16-le") + b"\x00\x00")
    sfi = pad4(struct.pack("<HHH", 0, 0, 1) + sfi_key + st)
    sfi_wlen = len(sfi)
    sfi = struct.pack("<HHH", sfi_wlen, 0, 1) + sfi_key + st
    sfi = pad4(sfi)

    vfi_key = pad4("VarFileInfo".encode("utf-16-le") + b"\x00\x00")
    var_val = struct.pack("<HH", 0x0409, 1252)
    vfi = struct.pack("<HHH", 0, 0,1) + vfi_key + var_val
    vfi = pad4(vfi)
    vfi_wlen = len(vfi)
    vfi = struct.pack("<HHH", vfi_wlen, len(var_val), 1) + vfi_key + var_val
    vfi = pad4(vfi)

    ffi = struct.pack("<I", 0xFEEF04BD)
    ffi += struct.pack("<I", 0x00010000)
    ffi += struct.pack("<HHHH", *filever)
    ffi += struct.pack("<HHHH", *prodver)
    ffi += struct.pack("<I", 0x0000003F)
    ffi += struct.pack("<I", 0)
    ffi += struct.pack("<I", 0x00000004)
    ffi += struct.pack("<I", 0x00000001)
    ffi += struct.pack("<I", 0)
    ffi += struct.pack("<I", 0)
    ffi += struct.pack("<I", 0)
    ffi = pad4(ffi)

    vk = pad4("VS_VERSION_INFO".encode("utf-16-le") + b"\x00\x00")
    body = ffi + sfi + vfi
    total = 2 + 2 + 2 + len(vk) + len(body)
    return struct.pack("<HHH", total, 52, 0) + vk + body


def _table(named, ids):
    # 资源目录表：Characteristics(4) + TimeDateStamp(4) + Major(2) + Minor(2) + NamedEntries(2) + IdEntries(2) = 16
    return (struct.pack("<I", 0) + struct.pack("<I", 0)
            + struct.pack("<H", 0) + struct.pack("<H", 0)
            + struct.pack("<H", named) + struct.pack("<H", ids))


def _entry(_id, off, is_dir):
    return struct.pack("<I", _id) + struct.pack("<I", (0x80000000 | off) if is_dir else off)


def _data_entry(rva, size):
    return struct.pack("<I", rva) + struct.pack("<I", size) + struct.pack("<I", 1252) + struct.pack("<I", 0)


def build_resource_section(manifest_bytes, vinfo_bytes):
    """重建 .rsrc 段：RT_MANIFEST(id24) + RT_VERSION(id16)，三级目录结构。"""
    root_off = 0
    root_end = len(_table(0, 2)) + 2 * 8  # 32

    m_lvl1 = root_end                  # 32
    m_lvl1_end = m_lvl1 + len(_table(0, 1)) + 8
    m_lvl2 = m_lvl1_end
    m_lvl2_end = m_lvl2 + len(_table(0, 1)) + 8
    m_dataentry = m_lvl2_end
    m_data = m_dataentry + 16
    m_end = m_data + len(manifest_bytes)

    v_lvl1 = m_end
    while v_lvl1 % 4:
        v_lvl1 += 1
    v_lvl1_end = v_lvl1 + len(_table(0, 1)) + 8
    v_lvl2 = v_lvl1_end
    v_lvl2_end = v_lvl2 + len(_table(0, 1)) + 8
    v_dataentry = v_lvl2_end
    v_data = v_dataentry + 16
    v_end = v_data + len(vinfo_bytes)

    blob = bytearray()
    blob += _table(0, 2)
    blob += _entry(24, m_lvl1, True)     # RT_MANIFEST
    blob += _entry(16, v_lvl1, True)     # RT_VERSION
    # manifest lvl1 -> lvl2
    blob += _table(0, 1)
    blob += _entry(1, m_lvl2, True)
    # manifest lvl2 -> dataentry
    blob += _table(0, 1)
    blob += _entry(0x0409, m_dataentry, False)
    # manifest data entry
    blob += _data_entry(m_data, len(manifest_bytes))
    blob += manifest_bytes
    # pad to v_lvl1
    while len(blob) < v_lvl1:
        blob += b"\x00"
    # version lvl1 -> lvl2
    blob += _table(0, 1)
    blob += _entry(1, v_lvl2, True)
    # version lvl2 -> dataentry
    blob += _table(0, 1)
    blob += _entry(0x0409, v_dataentry, False)
    # version data entry
    blob += _data_entry(v_data, len(vinfo_bytes))
    blob += vinfo_bytes

    return bytes(blob), v_end


def patch(exe_path, company, copyright, product, file_desc, filever=(1, 2, 0, 0), prodver=(1, 2, 0, 0)):
    pe = pefile.PE(exe_path, fast_load=False)
    pe.parse_data_directories(pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_RESOURCE"])

    manifest_bytes = b""
    for top in pe.DIRECTORY_ENTRY_RESOURCE.entries:
        if top.id == pefile.RESOURCE_TYPE["RT_MANIFEST"]:
            node = top.directory.entries[0]
            leaf = node.directory.entries[0]
            manifest_bytes = pe.get_data(
                leaf.data.struct.OffsetToData, leaf.data.struct.Size)

    vinfo = version_info(filever, prodver, copyright, product, company, file_desc)
    blob, _ = build_resource_section(manifest_bytes, vinfo)

    rsrc = None
    for s in pe.sections:
        if s.Name.rstrip(b"\x00") == b".rsrc":
            rsrc = s
            break
    if rsrc is None:
        raise RuntimeError("找不到 .rsrc 段")

    if len(blob) > rsrc.SizeOfRawData:
        raise RuntimeError("新资源大于原 .rsrc 段，需要扩展文件（当前不会触发）")

    # 直接覆盖 .rsrc 段原始数据（新资源小于原段，尾部旧数据不被引用，可留可删）
    raw_off = rsrc.PointerToRawData
    with open(exe_path, "r+b") as f:
        f.seek(raw_off)
        f.write(blob)

    print("已写入版本资源：版权=%s，产品=%s，版本=%d.%d.%d.%d"
          % (copyright, product, *filever))


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        print("用法: pe_set_version.py <exe> [--company C] [--copyright Y] [--product P] [--desc D]")
        sys.exit(1)
    exe = args[0]
    company = "Rainclouds"
    copyright = "Copyright © Rainclouds"
    product = "Rainclouds Minecraft Launcher 社区版"
    desc = "Rainclouds Minecraft Launcher"
    filever = (1, 2, 0, 0)
    prodver = (1, 2, 0, 0)
    i = 1
    while i < len(args):
        a = args[i]
        if a == "--company":
            company = args[i + 1]; i += 2
        elif a == "--copyright":
            copyright = args[i + 1]; i += 2
        elif a == "--product":
            product = args[i + 1]; i += 2
        elif a == "--desc":
            desc = args[i + 1]; i += 2
        elif a == "--filever":
            filever = tuple(int(x) for x in args[i + 1].split(".")); i += 2
        elif a == "--prodver":
            prodver = tuple(int(x) for x in args[i + 1].split(".")); i += 2
        else:
            i += 1
    patch(exe, company, copyright, product, desc, filever, prodver)
