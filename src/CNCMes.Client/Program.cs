namespace CNCMes.Client;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length > 0 && args[0] == "--capture")
        {
            TestScreenCapture.CaptureForm();
            return;
        }
        if ((args.Length > 0 && args[0] == "--workflow-direct") || Environment.GetEnvironmentVariable("CNC_DIRECT_WORKFLOW") == "1")
        {
            var api = new ApiClient();
            var session = new UserSession
            {
                Username = "a",
                DisplayName = "유과장",
                Role = "Worker",
                MachineId = "R01",
                SelectedMachines = new List<string> { "R01", "R02" }
            };
            Application.Run(new Workflow.WorkflowMainForm(api, session));
        }
        else
        {
            Application.Run(new LoginForm());
        }
    }
}
