namespace VRPhoneScreenOverlay.SteamVR.BindingTool;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "configure-startup" && args[1] is "enabled" or "disabled")
        {
            OpenVrBindingResult registration = OpenVrStartupRegistration.ConfigureInUtility(args[1] == "enabled");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(registration));
            return registration.Succeeded ? 0 : 1;
        }
        if (args.Length == 2 && args[0] == "inspect-controller" && args[1] is "left" or "right")
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(OpenVrControllerDiscovery.Inspect(
                args[1] == "left" ? OpenVrControllerHand.Left : OpenVrControllerHand.Right)));
            return 0;
        }
        if (args.Length == 2 && args[0] == "enrich-local-binding" && args[1] is "left" or "right")
        {
            return OpenVrBindingSettingsCleaner.EnrichLocalBinding(args[1] == "left" ? OpenVrControllerHand.Left : OpenVrControllerHand.Right).Succeeded ? 0 : 1;
        }
        if (args.Length != 1 ||
            !string.Equals(args[0], "activate-local-binding", StringComparison.Ordinal))
        {
            return 2;
        }

        OpenVrBindingResult result = OpenVrBindingSettingsCleaner.ActivateLocalBinding();
        return result.Succeeded ? 0 : 1;
    }
}
