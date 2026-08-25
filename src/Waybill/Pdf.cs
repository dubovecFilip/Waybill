using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace Waybill;

/// <summary>
/// The sheets, written out as one document.
///
/// A waybill that leaves the app as loose images is a delivery in pieces: two files
/// to keep together, two to attach, two to print, and nothing saying which of them
/// comes first. As a PDF it is the thing it has been drawn as all along, a document
/// with a page one and a page two, and it opens the same way on every machine it is
/// sent to.
///
/// Written by hand rather than by a library, because what is needed here is the
/// narrow end of the format: a fixed number of A4 pages, one picture on each, filling
/// the sheet. That is a few hundred bytes of scaffolding around images the app has
/// already drawn, and it is not worth a dependency, an installer entry and a licence
/// file to have somebody else emit them.
/// </summary>
public static class Pdf {
    // A4 in points, which is the only unit the format measures a page in.
    private const float PageW = 595.276f, PageH = 841.89f;

    /// <summary>Writes the pages, in order, one to a sheet.</summary>
    public static void Write(IReadOnlyList<Bitmap> pages, string path) {
        if (pages.Count == 0) throw new ArgumentException("A document with no pages in it is not a document.", nameof(pages));

        // The pictures first, so their size is known before anything is written.
        var jpegs = pages.Select(Jpeg).ToArray();

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        // Latin-1 rather than UTF-8: the scaffolding is ASCII, and a picture is bytes
        // that must not be re-encoded on the way past.
        var ascii = Encoding.Latin1;
        var offsets = new List<long>();

        void Text(string s) { var b = ascii.GetBytes(s); file.Write(b, 0, b.Length); }
        void Obj(int id) { while (offsets.Count <= id) offsets.Add(0); offsets[id] = file.Position; Text($"{id} 0 obj\n"); }

        // Three objects to a page: the page itself, the one line that says where to
        // put the picture, and the picture.
        int Page(int i) => 3 + i * 3;

        Text("%PDF-1.4\n");
        // A comment of high bytes, which is how a file says it is not to be treated
        // as text by anything that copies it around.
        file.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 6);

        Obj(1);
        Text("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        Obj(2);
        var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{Page(i)} 0 R"));
        Text($"<< /Type /Pages /Kids [ {kids} ] /Count {pages.Count} >>\nendobj\n");

        for (var i = 0; i < pages.Count; i++) {
            var page = Page(i);

            Obj(page);
            Text($"<< /Type /Page /Parent 2 0 R /MediaBox [ 0 0 {PageW:0.###} {PageH:0.###} ] "
                 + $"/Resources << /XObject << /Im0 {page + 2} 0 R >> >> /Contents {page + 1} 0 R >>\nendobj\n");

            // The picture is drawn into a unit square, so the matrix that places it is
            // the page itself: scaled to the sheet, set at the corner.
            var draw = $"q {PageW:0.###} 0 0 {PageH:0.###} 0 0 cm /Im0 Do Q\n";
            Obj(page + 1);
            Text($"<< /Length {ascii.GetByteCount(draw)} >>\nstream\n{draw}endstream\nendobj\n");

            Obj(page + 2);
            Text($"<< /Type /XObject /Subtype /Image /Width {pages[i].Width} /Height {pages[i].Height} "
                 + $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegs[i].Length} >>\nstream\n");
            file.Write(jpegs[i], 0, jpegs[i].Length);
            Text("\nendstream\nendobj\n");
        }

        // The index. Every entry is twenty bytes wide, counted rather than written
        // freely, because a reader seeks into this table by multiplying.
        var start = file.Position;
        Text($"xref\n0 {offsets.Count}\n");
        Text("0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++) Text($"{offsets[i]:0000000000} 00000 n \n");
        Text($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{start}\n%%EOF\n");
    }

    /// <summary>
    /// One sheet, as the picture that goes inside the document.
    ///
    /// JPEG rather than anything lossless, and this is the one place the paper earns
    /// it: the sheet is a photograph of a form, ruled lines over a stock that is
    /// deliberately uneven, and stored without loss that texture costs about twenty
    /// megabytes a page. At this quality nothing on the page can be told apart from
    /// the original by eye, and the whole document fits in an email.
    /// </summary>
    private static byte[] Jpeg(Bitmap page) {
        var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var buffer = new MemoryStream();
        if (codec == null) {
            page.Save(buffer, ImageFormat.Jpeg);
            return buffer.ToArray();
        }
        using var settings = new EncoderParameters(1);
        settings.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
        page.Save(buffer, codec, settings);
        return buffer.ToArray();
    }
}
