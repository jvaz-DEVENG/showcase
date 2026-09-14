using System.Runtime.InteropServices;
using System.Text;
using GameBoost.Core.Modules.Network;

namespace GameBoost.Core.Native;

/// <summary>
/// Lê SSID, sinal, canal e banda do Wi-Fi conectado pela API nativa do WLAN.
///
/// A alternativa óbvia seria rodar `netsh wlan show interfaces` e ler a saída,
/// e é o que quase toda ferramenta faz. Só que essa saída é **traduzida**: num
/// Windows em português os rótulos são "Sinal" e "Canal", em inglês são "Signal"
/// e "Channel", e o parser quebra ao trocar de idioma. A API devolve números.
/// </summary>
public static class WlanInfo
{
    private const uint ClientVersion = 2;

    private enum WLAN_INTF_OPCODE : uint
    {
        wlan_intf_opcode_current_connection = 7
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_INTERFACE_INFO
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strInterfaceDescription;
        public uint isState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_INTERFACE_INFO_LIST_HEADER
    {
        public uint dwNumberOfItems;
        public uint dwIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DOT11_SSID
    {
        public uint uSSIDLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] ucSSID;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_ASSOCIATION_ATTRIBUTES
    {
        public DOT11_SSID dot11Ssid;
        public uint dot11BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] dot11Bssid;
        public uint dot11PhyType;
        public uint uDot11PhyIndex;
        public uint wlanSignalQuality;
        public uint ulRxRate;
        public uint ulTxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_CONNECTION_ATTRIBUTES
    {
        public uint isState;
        public uint wlanConnectionMode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strProfileName;
        public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved,
        out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid,
        WLAN_INTF_OPCODE OpCode, IntPtr pReserved, out uint pdwDataSize,
        out IntPtr ppData, IntPtr pWlanOpcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr pMemory);

    /// <summary>
    /// <paramref name="idDoAdaptador"/> é o Id do NetworkInterface, que no
    /// Windows é o GUID da interface entre chaves.
    /// </summary>
    public static EstadoWifi? Ler(string idDoAdaptador)
    {
        if (!Guid.TryParse(idDoAdaptador.Trim('{', '}'), out var alvo))
            return null;

        var handle = IntPtr.Zero;
        var lista = IntPtr.Zero;

        try
        {
            if (WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out handle) != 0)
                return null;

            if (WlanEnumInterfaces(handle, IntPtr.Zero, out lista) != 0)
                return null;

            var cabecalho = Marshal.PtrToStructure<WLAN_INTERFACE_INFO_LIST_HEADER>(lista);
            var tamanhoItem = Marshal.SizeOf<WLAN_INTERFACE_INFO>();

            for (var i = 0; i < cabecalho.dwNumberOfItems; i++)
            {
                var endereco = IntPtr.Add(lista, 8 + i * tamanhoItem);
                var info = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(endereco);

                if (info.InterfaceGuid != alvo)
                    continue;

                return Consultar(handle, info.InterfaceGuid);
            }

            return null;
        }
        finally
        {
            if (lista != IntPtr.Zero)
                WlanFreeMemory(lista);

            if (handle != IntPtr.Zero)
                WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    private static EstadoWifi? Consultar(IntPtr handle, Guid interfaceGuid)
    {
        var dados = IntPtr.Zero;

        try
        {
            var guid = interfaceGuid;

            if (WlanQueryInterface(handle, ref guid, WLAN_INTF_OPCODE.wlan_intf_opcode_current_connection,
                    IntPtr.Zero, out _, out dados, IntPtr.Zero) != 0)
            {
                return null;
            }

            var conexao = Marshal.PtrToStructure<WLAN_CONNECTION_ATTRIBUTES>(dados);
            var assoc = conexao.wlanAssociationAttributes;

            var ssid = Encoding.UTF8.GetString(assoc.dot11Ssid.ucSSID, 0,
                (int)Math.Min(assoc.dot11Ssid.uSSIDLength, 32));

            var (banda, canal) = BandaECanal(assoc.dot11PhyType, assoc.ulRxRate);

            return new EstadoWifi(
                string.IsNullOrWhiteSpace(ssid) ? "rede sem nome" : ssid,
                (int)assoc.wlanSignalQuality,
                banda,
                canal,
                Padrao(assoc.dot11PhyType));
        }
        finally
        {
            if (dados != IntPtr.Zero)
                WlanFreeMemory(dados);
        }
    }

    /// <summary>
    /// A API de conexão atual não devolve o canal diretamente — ele está na
    /// lista de BSS, que é outra chamada e outra permissão. O que dá para
    /// afirmar sem inventar é o padrão de rádio, e é isso que sai daqui.
    /// </summary>
    private static (string Banda, int Canal) BandaECanal(uint phyType, uint taxaKbps)
    {
        // 802.11a e ac só existem em 5 GHz; b e g só em 2,4. n e ax vivem nas
        // duas, e aí a taxa é a única pista honesta que sobra.
        var banda = phyType switch
        {
            2 or 8 => "5 GHz",                 // dot11_phy_type_ofdm, vht
            4 or 5 => "2,4 GHz",               // hrdsss, erp
            1 => "2,4 GHz",                    // fhss
            _ => taxaKbps > 300_000 ? "5 GHz ou 6 GHz" : "banda não identificada"
        };

        return (banda, 0);
    }

    private static string Padrao(uint phyType) => phyType switch
    {
        1 => "802.11 (FHSS)",
        2 => "802.11a",
        4 => "802.11b",
        5 => "802.11g",
        7 => "802.11n",
        8 => "802.11ac",
        9 => "802.11ad",
        10 => "802.11ax (Wi-Fi 6)",
        11 => "802.11be (Wi-Fi 7)",
        _ => "padrão não identificado"
    };
}
