using System.Runtime.CompilerServices;
using System.Text;

namespace Soovioo.Api.Tests;

/// <summary>Mirrors Program.cs's registration so tests can decode the
/// Windows-1250 matrika XML output without starting the whole app.</summary>
internal static class TestEncodingSetup
{
    [ModuleInitializer]
    public static void Register() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
}
