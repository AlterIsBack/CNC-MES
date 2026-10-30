using System.Drawing.Imaging;
using CNCMes.Client.Workflow;
using CNCMes.Client.Workflow.Models;

namespace CNCMes.Client;

public static class TestScreenCapture
{
    public static void CaptureForm()
    {
        ApplicationConfiguration.Initialize();
        var api = new ApiClient();
        var session = new UserSession
        {
            Username = "a",
            DisplayName = "유과장",
            Role = "Worker",
            MachineId = "R01",
            SelectedMachines = new System.Collections.Generic.List<string> { "R01", "R02" }
        };

        var form = new WorkflowMainForm(api, session);
        form.Width = 1920;
        form.Height = 1080;
        form.FormBorderStyle = FormBorderStyle.None;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);

        form.Show();
        for (int i = 0; i < 15; i++)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(50);
        }

        // Simulate user clicking "기본 워크플로우 생성" twice:
        // 1st Lane: R01 primary workflow
        form.GenerateStandardWorkflowForSelectedPart(confirmIfNotEmpty: false);
        // 2nd Lane: R02 parallel multi-machine workflow
        form.GenerateStandardWorkflowForSelectedPart(confirmIfNotEmpty: false);

        for (int i = 0; i < 15; i++)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(50);
        }

        using (var bmp = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, 1920, 1080));
            string path = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\workflow_screen_nodes_1080.png";
            bmp.Save(path, ImageFormat.Png);
            Console.WriteLine("SAVED_NODES_SCREEN: " + path);
        }
        form.Close();
    }
}
