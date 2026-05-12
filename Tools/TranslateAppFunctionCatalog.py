import json
from pathlib import Path


CAT_ZH = {
    "Protection": "保护",
    "Control": "控制",
    "Condition Monitoring and Supervision": "状态监测与监视",
    "Measurement": "测量",
    "Power Quality": "电能质量",
    "Traditional LED indication": "传统LED指示",
    "Logging functions": "记录功能",
    "Other functionality": "其他功能",
}

CODE_ZH = {
    "ARCSARC": "弧光保护",
    "MAPGAPC": "多用途保护",
    "TRPPTRC": "主跳闸",
    "CBXCBR": "断路器控制",
    "DARREC": "自动重合闸",
    "DCSXSWI": "隔离开关位置指示",
    "DCXSWI": "隔离开关控制",
    "ESMGAPC": "紧急启动",
    "ESSXSWI": "接地开关位置指示",
    "ESXSWI": "接地开关控制",
    "OL5ATCC": "带电压调节器的分接开关控制",
    "OLATCC": "带电压调节器的分接开关控制（传统）",
    "OLGAPC": "变压器数据合成",
    "SECRSYN": "同期及带电检查",
    "TPOSYLTC": "分接开关位置指示",
    "UPCALH": "断路器位置不一致启动",
    "CCSPVC": "电流回路监视",
    "CTSRCTF": "变压器电流回路监视",
    "ESDCSSWI": "电动接地开关和隔离开关监视",
    "HSARSPTR": "变压器热点和绝缘老化率监测",
    "HZCCASPVC": "高阻抗保护方案A相电流互感器监视",
    "HZCCBSPVC": "高阻抗保护方案B相电流互感器监视",
    "HZCCCSPVC": "高阻抗保护方案C相电流互感器监视",
    "LNCTSRCTF": "线路差动电流回路监视",
    "MDSOPT": "机器和设备运行时间计数器",
    "MSVPR": "三相残压欠电压监视",
    "PCSITPC": "保护通信监视",
    "PHSVPR": "电压存在监视",
    "SEQSPVC": "熔断器失效监视",
    "SSCBR": "断路器状态监测",
    "TCSSCBR": "跳闸回路监视",
    "CMMXU": "三相电流测量",
    "CSMSQI": "序电流测量",
    "FMMXU": "频率测量",
    "LDPRLRC": "负荷曲线记录器",
    "PEMMXU": "三相功率和电能测量",
    "RESCMMXU": "剩余电流测量",
    "RESVMMXU": "剩余电压测量",
    "SPEMMXU": "单相功率和电能测量",
    "VAMMXU": "单相电压测量",
    "VMMXU": "三相电压测量",
    "VPHMMXU": "相电压测量",
    "VSMSQI": "序电压测量",
    "CHMHAI": "电流总需求畸变、谐波畸变、直流分量和各次谐波",
    "PHQVVR": "电压波动",
    "VHMHAI": "电压总谐波畸变、直流分量和各次谐波",
    "VSQVUB": "电压不平衡",
    "LED": "单个可编程LED控制",
    "LEDPTRC": "LED指示控制",
    "A1RADR": "扰动记录器模拟通道1...12",
    "B1RBDR": "扰动记录器开关量通道1...32",
    "B2RBDR": "扰动记录器开关量通道33...64",
    "FLTRFRC": "故障记录器",
    "RDRE": "扰动记录器（公共功能）",
    "SER": "顺序事件记录器",
    "ADDR": "实数加法",
    "AND": "双输入与门",
    "AND20": "二十输入与门",
    "AND6": "六输入与门",
    "CALGAPC": "日历功能",
    "CMSUM": "电流求和",
    "CMSWI": "电流切换",
    "CONTROL": "就地/远方控制",
    "DIVR": "实数除法",
    "DNPLPRT": "DNP3协议",
    "DTMGAPC": "日定时器",
    "EQR": "实数相等比较器",
    "ETHLDEV": "以太网逻辑设备",
    "FALSE": "常量FALSE",
    "FKEY4GGIO": "可编程按键（4键）",
    "FKEYGGIO": "可编程按键（16键）",
    "FTPLPRT": "FTP配置",
    "F_TRIG": "下降沿检测器",
    "GATEGAPC": "可控门（8通道）",
    "GER": "实数大于等于比较器",
    "GNRLLTMS": "时间主站监视",
    "GOOSERCV_BIN": "接收GOOSE开关量信息",
    "GOOSERCV_CMV": "接收GOOSE测量值（相量）信息",
    "GOOSERCV_DP": "接收GOOSE双点开关量信息",
    "GOOSERCV_ENUM": "接收GOOSE枚举值信息",
    "GOOSERCV_INT32": "接收GOOSE 32位整数值信息",
    "GOOSERCV_INT8": "接收GOOSE 8位整数值信息",
    "GOOSERCV_INTL": "接收GOOSE闭锁信息",
    "GOOSERCV_MV": "接收GOOSE测量值信息",
    "GSAL": "安全应用",
    "GSELPRT": "GSELPRT通信功能",
    "HLTGAPC": "带电作业标记",
    "HMILCCH": "HMI通信通道",
    "HMILDEV": "HMI设备",
    "HTTPLPRT": "HTTP配置",
    "I3CLPRT": "IEC 60870-5-103协议",
    "I5CLPRT": "IEC 60870-5-104协议",
    "ILTCTR": "相电流预处理",
    "LER": "实数小于等于比较器",
    "MAX3R": "实数最大值选择器",
    "MBMLPRT": "Modbus协议（主站）",
    "MBSLPRT": "Modbus协议（从站）",
    "MIN3R": "实数最小值选择器",
    "MINMAXAVE12R": "最小值、最大值和平均值计算器",
    "MMSLPRT": "MMS通信功能",
    "MMVF4GAPC": "接收Modbus测量值",
    "MMVGAPC": "接收Modbus开关量值",
    "MMVI4GAPC": "接收Modbus 32位整数值",
    "MULR": "实数乘法",
    "MVGAPC": "布尔值事件生成",
    "MVI4GAPC": "整数值事件生成",
    "NER": "实数不等比较器",
    "NOT": "非门",
    "OR": "双输入或门",
    "OR20": "二十输入或门",
    "OR6": "六输入或门",
    "PROTECTION": "参数定值组",
    "PTGAPC": "脉冲定时器（8通道）",
    "QTY_BAD": "信号质量差",
    "QTY_GOOD": "信号质量好",
    "QTY_GOOSE_COMM": "GOOSE通信质量",
    "QTY_GOOSE_TEST": "接收GOOSE测试模式",
    "RCHLCCH": "冗余以太网通道监视",
    "RESTCTR": "剩余电流预处理",
    "RS": "RS触发器（易失）",
    "R_TRIG": "上升沿检测器",
    "SCA4GAPC": "带比例缩放的模拟值事件生成",
    "SCHLCCH": "以太网通道监视",
    "SERLCCH": "事件记录通信通道",
    "SETI32GAPC": "16个可设置32位整数值",
    "SETRGAPC": "16个可设置实数值",
    "SMVRCV": "SMV流接收器",
    "SMVSENDER": "SMV流发送器（IEC 61850-9-2LE）",
    "SMVSENDER61869": "SMV流发送器（IEC 61869-9）",
    "SMV_QUALITY": "SMV流通道质量解码器",
    "SPCGAPC": "通用控制点",
    "SPCLGAPC": "本地通用控制点",
    "SPCRGAPC": "远方通用控制点",
    "SR": "SR触发器（易失）",
    "SRGAPC": "SR触发器（8通道，非易失）",
    "SUBR": "实数减法",
    "SWITCHI32": "32位整数切换选择器",
    "SWITCHR": "实数切换选择器",
    "TOFGAPC": "关断延时（8通道）",
    "TONGAPC": "接通延时（8通道）",
    "TPGAPC": "最小脉冲定时器（2通道）",
    "TPMGAPC": "分钟级最小脉冲定时器（2通道）",
    "TPSGAPC": "秒级最小脉冲定时器（2通道）",
    "TRUE": "常量TRUE",
    "T_B16_TO_I32": "布尔量到32位整数转换",
    "T_BIN_TCMD": "二进制命令到32位整数转换",
    "T_DIR": "故障方向评估",
    "T_DIR_FWD": "正向故障方向评估",
    "T_DIR_REV": "反向故障方向评估",
    "T_HEALTH": "GOOSE数据健康状态",
    "T_I32_TO_B16": "32位整数到布尔量转换",
    "T_I32_TO_R": "32位整数到实数转换",
    "T_I8_TO_I32": "8位整数到32位整数转换",
    "T_POS_CL": "开关设备状态解码器：合位",
    "T_POS_OK": "开关设备状态解码器：状态正常",
    "T_POS_OP": "开关设备状态解码器：分位",
    "T_R_TO_I32": "实数到32位整数转换",
    "T_R_TO_I8": "实数到8位整数转换",
    "T_TCMD": "枚举量到布尔量转换",
    "T_TCMD_BIN": "32位整数到二进制命令转换",
    "UDFCNT": "通用加减计数器",
    "UTVTR": "相电压和剩余电压预处理",
    "VMSWI": "电压切换",
    "XOR": "双输入异或门",
}

EXTRA_ALIASES = {
    "ARCSARC": ["弧光", "AFD"],
    "MAPGAPC": ["多用途", "通用保护"],
    "TRPPTRC": ["跳闸", "主跳"],
    "CBXCBR": ["断路器", "开关控制", "52"],
    "DARREC": ["重合闸", "79"],
    "DCSXSWI": ["隔离开关", "位置指示"],
    "DCXSWI": ["隔离开关", "控制"],
    "ESSXSWI": ["接地开关", "位置指示"],
    "ESXSWI": ["接地开关", "控制"],
    "SECRSYN": ["同期检查", "合闸检查", "25"],
    "CCSPVC": ["电流回路", "回路监视"],
    "SEQSPVC": ["熔丝失败", "熔断器", "60"],
    "TCSSCBR": ["跳闸回路", "TCM"],
    "SSCBR": ["断路器监测", "52CM"],
    "CMMXU": ["电流测量"],
    "VMMXU": ["电压测量"],
    "FMMXU": ["频率"],
    "LDPRLRC": ["负荷曲线", "负载曲线"],
    "PEMMXU": ["功率", "电能"],
    "RESCMMXU": ["零序电流", "剩余电流"],
    "RESVMMXU": ["零序电压", "剩余电压"],
    "VSQVUB": ["不平衡", "电压不平衡"],
    "CHMHAI": ["谐波", "电流谐波", "THD"],
    "VHMHAI": ["谐波", "电压谐波", "THD"],
    "RDRE": ["录波", "扰动记录"],
    "FLTRFRC": ["故障记录"],
    "SER": ["事件记录", "SOE"],
    "CONTROL": ["本地远方", "就地远方"],
    "GOOSERCV_BIN": ["GOOSE", "开关量"],
    "GOOSERCV_CMV": ["GOOSE", "相量"],
    "GOOSERCV_MV": ["GOOSE", "测量值"],
    "MMVI4GAPC": ["Modbus", "32位整数"],
    "SMVRCV": ["采样值", "SMV接收"],
    "SMVSENDER": ["采样值", "SMV发送"],
    "SMVSENDER61869": ["采样值", "SMV发送"],
    "SMV_QUALITY": ["采样值质量", "SMV质量"],
}


def clean_aliases(function: dict, zh_name: str, cat_zh: str) -> list[str]:
    aliases: list[str] = []
    for alias in function.get("ChineseAliases", []):
        if not isinstance(alias, str):
            continue
        alias = alias.strip()
        if not alias or set(alias) <= {"?"}:
            continue
        if alias == function.get("EnglishName", ""):
            continue
        aliases.append(alias)

    aliases.extend([zh_name, cat_zh, function.get("Code", ""), function.get("Ansi", "")])
    aliases.extend(EXTRA_ALIASES.get(function.get("Code", ""), []))

    result: list[str] = []
    seen: set[str] = set()
    for alias in aliases:
        if not alias:
            continue
        for part in str(alias).replace("，", ",").split(","):
            value = part.strip()
            if not value or value in seen:
                continue
            seen.add(value)
            result.append(value)
    return result


def main() -> None:
    path = Path("Rex615OfflineConfigurator/Data/AppFunctionCatalog.json")
    data = json.loads(path.read_text(encoding="utf-8-sig"))

    updated = 0
    missing: list[tuple[str, str, str]] = []
    for version in data["Versions"]:
        version_name = version["Version"]
        for function in version["Functions"]:
            if not function.get("IsBase"):
                continue

            code = function.get("Code", "")
            zh_name = CODE_ZH.get(code)
            if not zh_name:
                missing.append((version_name, code, function.get("EnglishName", "")))
                continue

            if code == "SMVRCV" and "16 channels" in function.get("EnglishName", ""):
                zh_name = "SMV流接收器（16通道）"

            cat_zh = CAT_ZH.get(function.get("Category", ""), function.get("Category", ""))
            function["ChineseName"] = zh_name
            function["ChineseAliases"] = clean_aliases(function, zh_name, cat_zh)
            function["PrincipleSummary"] = (
                f"功能说明：{zh_name}。该功能属于{cat_zh}，在 {version_name} 中作为基础功能提供。"
            )

            source = function.get("PrincipleSource", "")
            if source.startswith("ABB REX615 Product Guide"):
                functionality_code = function.get("FunctionalityCode") or code
                function["PrincipleSource"] = (
                    f"ABB REX615 产品指南 {version_name} - 基础及可选功能；"
                    f"ABB 技术手册 - {functionality_code} Functionality"
                )
            updated += 1

    if missing:
        for item in missing:
            print(f"Missing translation: {item[0]} {item[1]} {item[2]}")
        raise SystemExit(1)

    data["GeneratedAt"] = "2026-05-12T05:10:00+00:00"
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Updated {updated} base function translations.")


if __name__ == "__main__":
    main()
