using System.Text;
using FluentAssertions;
using Nop.Plugin.Misc.CustomerProfile.Logic;
using NUnit.Framework;

namespace Nop.Plugin.Misc.CustomerProfile.Tests;

public class UploadValidatorTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n...");
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 };
    private static readonly byte[] Exe = { (byte)'M', (byte)'Z', 0x90, 0x00 };

    [Test]
    public void Pdf_is_accepted_with_content_type_from_bytes()
    {
        UploadValidator.Validate("form.pdf", Pdf, 10).Should().Be((UploadProblem.None, "application/pdf"));
    }

    [Test]
    public void Png_and_jpeg_are_accepted()
    {
        UploadValidator.Validate("scan.png", Png, 10).Should().Be((UploadProblem.None, "image/png"));
        UploadValidator.Validate("scan.JPG", Jpeg, 10).Should().Be((UploadProblem.None, "image/jpeg"));
        UploadValidator.Validate("scan.jpeg", Jpeg, 10).Should().Be((UploadProblem.None, "image/jpeg"));
    }

    [Test]
    public void Extension_must_match_content()
    {
        UploadValidator.Validate("form.png", Pdf, 10).Problem.Should().Be(UploadProblem.UnsupportedType);
    }

    [Test]
    public void Renamed_executable_is_rejected()
    {
        UploadValidator.Validate("form.pdf", Exe, 10).Problem.Should().Be(UploadProblem.UnsupportedType);
    }

    [Test]
    public void Unknown_extension_is_rejected()
    {
        UploadValidator.Validate("form.docx", Pdf, 10).Problem.Should().Be(UploadProblem.UnsupportedType);
    }

    [Test]
    public void Empty_file_is_rejected()
    {
        UploadValidator.Validate("form.pdf", Array.Empty<byte>(), 10).Problem.Should().Be(UploadProblem.Empty);
    }

    [Test]
    public void Size_limit_is_inclusive()
    {
        var atLimit = new byte[10 * 1024 * 1024];
        Pdf.CopyTo(atLimit, 0);
        UploadValidator.Validate("form.pdf", atLimit, 10).Problem.Should().Be(UploadProblem.None);

        var overLimit = new byte[10 * 1024 * 1024 + 1];
        Pdf.CopyTo(overLimit, 0);
        UploadValidator.Validate("form.pdf", overLimit, 10).Problem.Should().Be(UploadProblem.TooLarge);
    }
}
