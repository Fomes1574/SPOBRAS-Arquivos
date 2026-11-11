using System.Buffers.Binary;
using System.Globalization;

namespace Spobras.Documentos.Imagens;

/// <summary>Leitura minima do EXIF de um JPEG: orientacao e data de captura. Sem EXIF, devolve vazio.</summary>
public static class Exif
{
    public sealed record Dados(int Orientacao, DateTime? CapturadaEm);

    public static Dados Ler(string caminho)
    {
        try
        {
            using var arquivo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Ler(arquivo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or ArgumentException)
        {
            return new Dados(1, null);
        }
    }

    public static Dados Ler(Stream jpeg)
    {
        var vazio = new Dados(1, null);
        Span<byte> cab = stackalloc byte[4];
        if (jpeg.Read(cab[..2]) != 2 || cab[0] != 0xFF || cab[1] != 0xD8)
        {
            return vazio;
        }

        // Percorre os segmentos ate achar o APP1 "Exif".
        while (jpeg.Read(cab) == 4 && cab[0] == 0xFF)
        {
            int marcador = cab[1];
            int tamanho = BinaryPrimitives.ReadUInt16BigEndian(cab[2..]) - 2;
            if (marcador == 0xDA || tamanho < 0)
            {
                break; // comecaram os dados da imagem
            }

            if (marcador == 0xE1 && tamanho >= 14 && tamanho < 1 << 20)
            {
                var bloco = new byte[tamanho];
                jpeg.ReadExactly(bloco);
                if (bloco.AsSpan(0, 6).SequenceEqual("Exif\0\0"u8))
                {
                    return LerTiff(bloco.AsSpan(6)) ?? vazio;
                }

                continue;
            }

            jpeg.Seek(tamanho, SeekOrigin.Current);
        }

        return vazio;
    }

    private static Dados? LerTiff(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
        {
            return null;
        }

        bool pequeno = tiff[0] == (byte)'I';
        ushort U16(ReadOnlySpan<byte> s, int p) => pequeno ? BinaryPrimitives.ReadUInt16LittleEndian(s[p..]) : BinaryPrimitives.ReadUInt16BigEndian(s[p..]);
        uint U32(ReadOnlySpan<byte> s, int p) => pequeno ? BinaryPrimitives.ReadUInt32LittleEndian(s[p..]) : BinaryPrimitives.ReadUInt32BigEndian(s[p..]);

        int orientacao = 1;
        DateTime? data = null;
        uint exifIfd = 0;

        void Percorrer(ReadOnlySpan<byte> s, uint inicio, bool principal, ref int orient, ref DateTime? quando, ref uint sub)
        {
            if (inicio + 2 > s.Length)
            {
                return;
            }

            int entradas = U16(s, (int)inicio);
            for (int i = 0; i < entradas; i++)
            {
                int p = (int)inicio + 2 + i * 12;
                if (p + 12 > s.Length)
                {
                    return;
                }

                ushort etiqueta = U16(s, p);
                if (principal && etiqueta == 0x0112)
                {
                    int valor = U16(s, p + 8);
                    orient = valor is >= 1 and <= 8 ? valor : 1;
                }
                else if (principal && etiqueta == 0x8769)
                {
                    sub = U32(s, p + 8);
                }
                else if ((!principal && etiqueta == 0x9003) || (principal && etiqueta == 0x0132 && quando is null))
                {
                    uint quantidade = U32(s, p + 4);
                    uint posicao = U32(s, p + 8);
                    if (quantidade >= 19 && posicao + 19 <= s.Length)
                    {
                        string texto = System.Text.Encoding.ASCII.GetString(s.Slice((int)posicao, 19));
                        if (DateTime.TryParseExact(texto, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lida))
                        {
                            quando = lida;
                        }
                    }
                }
            }
        }

        Percorrer(tiff, U32(tiff, 4), true, ref orientacao, ref data, ref exifIfd);
        if (exifIfd != 0)
        {
            DateTime? original = null;
            Percorrer(tiff, exifIfd, false, ref orientacao, ref original, ref exifIfd);
            data = original ?? data; // a data de captura vale mais que a data generica do arquivo
        }

        return new Dados(orientacao, data);
    }
}
