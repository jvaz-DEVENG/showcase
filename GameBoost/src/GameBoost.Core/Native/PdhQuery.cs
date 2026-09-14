using System.Runtime.InteropServices;

namespace GameBoost.Core.Native;

/// <summary>
/// Consulta PDH com curinga: um unico contador do tipo
/// "\GPU Engine(*engtype_3D)\Utilization Percentage" resolve todas as
/// instancias de uma vez.
///
/// Substitui dezenas de objetos PerformanceCounter, um por instancia, que
/// custavam mais de 1 segundo por ciclo numa maquina com jogo e navegadores
/// abertos: cada NextValue() e uma consulta separada ao subsistema.
///
/// Usa PdhAddEnglishCounter de proposito. O nome em ingles vale em qualquer
/// idioma do Windows, enquanto PerformanceCounter exige o nome traduzido e
/// quebraria neste Windows pt-BR.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_MORE_DATA = 0x800007D2;
    private const int TamanhoDoItem = 24; // x64: LPWSTR + DWORD + padding + double

    [DllImport("pdh.dll")]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(
        IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    private IntPtr _query;
    private IntPtr _contador;
    private uint _tamanhoDoBuffer;
    private IntPtr _buffer;
    private bool _descartado;

    public bool Valido => _query != IntPtr.Zero && _contador != IntPtr.Zero;

    private PdhQuery(IntPtr query, IntPtr contador)
    {
        _query = query;
        _contador = contador;
    }

    /// <summary>Abre a consulta. Devolve null se o contador nao existir nesta maquina.</summary>
    public static PdhQuery? Abrir(string caminhoDoContador)
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out var query) != 0)
            return null;

        if (PdhAddEnglishCounterW(query, caminhoDoContador, IntPtr.Zero, out var contador) != 0)
        {
            PdhCloseQuery(query);
            return null;
        }

        // A primeira coleta so estabelece a linha de base dos contadores de taxa.
        PdhCollectQueryData(query);

        return new PdhQuery(query, contador);
    }

    /// <summary>
    /// Valores atuais de todas as instancias. Lista vazia significa que nao ha
    /// instancia ativa no momento, o que e normal para engines de GPU ociosas.
    /// </summary>
    public IReadOnlyList<double> Coletar()
    {
        if (!Valido || _descartado)
            return Array.Empty<double>();

        if (PdhCollectQueryData(_query) != 0)
            return Array.Empty<double>();

        var tamanho = _tamanhoDoBuffer;
        var status = PdhGetFormattedCounterArrayW(_contador, PDH_FMT_DOUBLE, ref tamanho, out var quantidade, _buffer);

        if (status == PDH_MORE_DATA)
        {
            // Realoca so quando o numero de instancias cresce, nao a cada ciclo.
            if (_buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(_buffer);

            _tamanhoDoBuffer = tamanho;
            _buffer = Marshal.AllocHGlobal((int)tamanho);

            status = PdhGetFormattedCounterArrayW(_contador, PDH_FMT_DOUBLE, ref tamanho, out quantidade, _buffer);
        }

        if (status != 0 || quantidade == 0 || _buffer == IntPtr.Zero)
            return Array.Empty<double>();

        var valores = new List<double>((int)quantidade);
        for (var i = 0; i < quantidade; i++)
        {
            // Dentro de PDH_FMT_COUNTERVALUE_ITEM: nome (8) + CStatus (4) + padding (4).
            var valor = Marshal.PtrToStructure<double>(_buffer + i * TamanhoDoItem + 16);

            if (!double.IsNaN(valor) && !double.IsInfinity(valor))
                valores.Add(valor);
        }

        return valores;
    }

    public void Dispose()
    {
        if (_descartado)
            return;

        _descartado = true;

        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }

        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }

        _contador = IntPtr.Zero;
    }
}
