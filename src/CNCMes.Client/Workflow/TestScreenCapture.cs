using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
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
            DisplayName = "김철수 (작업자)",
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
        for (int i = 0; i < 20; i++)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(50);
        }

        // 1. Toast Notification Forms rendering test (신규 작업지시서, 공지사항, 채팅 알림)
        var toast1 = new ToastNotificationForm("📋 신규 작업지시서 등록", "새 작업지시서 [WO-2026-003] (삼성전자) 등록되었습니다.", ToastType.WorkOrder);
        toast1.Opacity = 0.95;
        toast1.Location = new Point(1920 - 380, 1080 - 100);
        toast1.Show();

        var toast2 = new ToastNotificationForm("📢 새 공지사항 알림", "[긴급공지] 오후 2시 공구 교체 및 안전 점검 진행 예정", ToastType.Notice);
        toast2.Opacity = 0.95;
        toast2.Location = new Point(1920 - 380, 1080 - 195);
        toast2.Show();

        var toast3 = new ToastNotificationForm("💬 새 현장 채팅", "[이반장]: R02 황삭 가공 완료되었습니다. 측정실 이송 부탁드립니다.", ToastType.Chat);
        toast3.Opacity = 0.95;
        toast3.Location = new Point(1920 - 380, 1080 - 290);
        toast3.Show();

        for (int k = 0; k < 15; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }

        // Capture single toast card preview
        using (var toastBmp = new Bitmap(toast1.Width, toast1.Height))
        {
            toast1.DrawToBitmap(toastBmp, new Rectangle(0, 0, toast1.Width, toast1.Height));
            string tPath = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\toast_card_v153.png";
            toastBmp.Save(tPath, ImageFormat.Png);
            Console.WriteLine("SAVED_TOAST_CARD: " + tPath);
        }

        // 2. Main Workflow Form Capture (with v1.5.3 top bar, refresh F5 button, auto-polling toggle, timeline interval)
        using (var bmp = new Bitmap(1920, 1080))
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, 1920, 1080));

            // Overlay active toast notifications onto full screen capture
            using (var g = Graphics.FromImage(bmp))
            {
                using var b1 = new Bitmap(toast1.Width, toast1.Height);
                toast1.DrawToBitmap(b1, new Rectangle(0, 0, toast1.Width, toast1.Height));
                g.DrawImage(b1, toast1.Location);

                using var b2 = new Bitmap(toast2.Width, toast2.Height);
                toast2.DrawToBitmap(b2, new Rectangle(0, 0, toast2.Width, toast2.Height));
                g.DrawImage(b2, toast2.Location);

                using var b3 = new Bitmap(toast3.Width, toast3.Height);
                toast3.DrawToBitmap(b3, new Rectangle(0, 0, toast3.Width, toast3.Height));
                g.DrawImage(b3, toast3.Location);
            }

            string path = @"C:\Users\USER\.gemini\antigravity\brain\c11cfd6b-e9b4-430b-acd3-767a137025e6\workflow_v153_main_screen.png";
            bmp.Save(path, ImageFormat.Png);
            Console.WriteLine("SAVED_V153_SCREEN: " + path);
        }

        toast1.Close();
        toast2.Close();
        toast3.Close();
        form.Close();
    }
}
