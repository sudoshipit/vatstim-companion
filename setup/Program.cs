using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlightCompanion;
[assembly:AssemblyVersion("0.1.0.0")]
[assembly:InternalsVisibleTo("FlightCompanion.Setup.Tests")]
namespace FlightCompanionSetup {
  internal sealed class SetupForm : Form {
    readonly TextBox token=new TextBox(),pairCode=new TextBox(),status=new TextBox();
    readonly Label instructions=new Label();
    readonly Button connect=new Button(),install=new Button(),launch=new Button();
    readonly CheckBox replies=new CheckBox(),disconnect=new CheckBox();
    readonly CancellationTokenSource cancel=new CancellationTokenSource();
    readonly string root,installedRoot;
    BotClient client;Pairing pairing;PluginSettings settings;bool installed,busy;
    internal SetupForm() {
      root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));
      installedRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"vPilot");
      Text="Flight Companion - private Telegram setup";ClientSize=new Size(650,570);StartPosition=FormStartPosition.CenterScreen;
      Font=new Font("Segoe UI",10);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;
      AddLabel("1. Paste the NEW bot token below (after revoking the exposed token).",18,18,610,28);
      token.SetBounds(18,52,610,30);token.UseSystemPasswordChar=true;Controls.Add(token);
      connect.Text="Verify bot and pair";connect.SetBounds(18,94,190,35);connect.Click+=async(s,e)=>await Pair();Controls.Add(connect);
      AddLabel("The token stays in memory. It is never written to files or permanent settings.",18,139,610,28);
      instructions.SetBounds(18,178,610,62);instructions.Text="2. After verification, send the pairing text below in a PRIVATE chat with your bot.";Controls.Add(instructions);
      pairCode.SetBounds(18,243,610,30);pairCode.ReadOnly=true;Controls.Add(pairCode);
      replies.SetBounds(18,290,600,28);replies.Text="Enable private replies to recent vPilot message notifications";Controls.Add(replies);
      disconnect.SetBounds(18,322,600,28);disconnect.Text="Enable remote disconnect (requires /disconnect followed by /confirm)";Controls.Add(disconnect);
      install.SetBounds(18,366,260,37);install.Text="3. Install for this paired chat";install.Enabled=false;install.Click+=(s,e)=>Install();Controls.Add(install);
      launch.SetBounds(296,366,300,37);launch.Text="4. Launch vPilot with this token";launch.Enabled=false;launch.Click+=(s,e)=>Launch();Controls.Add(launch);
      status.SetBounds(18,424,610,123);status.Multiline=true;status.ReadOnly=true;status.ScrollBars=ScrollBars.Vertical;Controls.Add(status);
      SetStatus("Ready. vPilot must be closed for installation. No VATSIM connection, messages or disconnect commands will be sent by setup.","awaiting-token");
      FormClosed+=(s,e)=>{cancel.Cancel();if(client!=null)client.Dispose();token.Clear();};
    }
    void AddLabel(string text,int x,int y,int w,int h){var label=new Label{Text=text};label.SetBounds(x,y,w,h);Controls.Add(label);}
    void SetStatus(string text,string stage) {
      if(IsDisposed)return;status.Text=text;
      // Only fixed stage names go to disk. No token, IDs, chat contents or server errors.
      try{File.WriteAllText(Path.Combine(root,"setup","setup-status.txt"),stage,new System.Text.UTF8Encoding(false));}catch{}
    }
    static long Now(){return (long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;}
    async Task Pair() {
      if(busy)return;busy=true;connect.Enabled=false;install.Enabled=false;launch.Enabled=false;
      if(client!=null){client.Dispose();client=null;}pairing=null;settings=null;installed=false;
      try {
        string supplied=token.Text.Trim();token.Clear();client=new BotClient(supplied);supplied=null;
        SetStatus("Checking the bot and webhook state...","verifying-token");
        string username=await client.Verify(cancel.Token);
        pairing=new Pairing(Now());pairCode.Text=pairing.Code;
        instructions.Text="2. Open @"+username+" in Telegram. Tap Start if necessary, then send EXACTLY the text below in your private chat. Expires in 5 minutes.";
        SetStatus("Bot verified. Waiting for your private pairing message. You can select and copy the pairing text above.","awaiting-pairing");
        while(!cancel.IsCancellationRequested&&Now()-pairing.Started<=300) {
          object updates=await client.Call("getUpdates",new {offset=pairing.Offset,timeout=15,limit=100,allowed_updates=new[]{"message"}},cancel.Token);
          if(pairing.Accept(updates,Now())) {
            pairCode.Text="Private chat verified";
            SetStatus("Paired successfully. Choose whether to enable replies and confirmed disconnect, then click Install. Only your private chat/user will be allowed.","paired");
            install.Enabled=true;return;
          }
          await Task.Delay(300,cancel.Token);
        }
        throw new SetupFailure("Pairing expired. Enter the token and verify again to create a fresh code.");
      }catch(OperationCanceledException){if(!IsDisposed)SetStatus("Setup cancelled or request timed out. No token was saved.","cancelled");}
      catch(SetupFailure ex){SetStatus(ex.Message,"setup-error");}
      catch {SetStatus("Setup could not complete. No token was saved. Check your connection and try again.","setup-error");}
      finally {busy=false;if(!IsDisposed)connect.Enabled=true;}
    }
    void Install() {
      try {
        if(pairing==null||pairing.ChatId<=0)throw new SetupFailure("Pair your private chat first.");
        if(Process.GetProcessesByName("vPilot").Length!=0)throw new SetupFailure("Close vPilot at a suitable time, then click Install again. Setup will not stop it.");
        string exe=Path.Combine(installedRoot,"vPilot.exe");
        if(!File.Exists(exe))throw new SetupFailure("The expected vPilot installation was not found.");
        if(FileVersionInfo.GetVersionInfo(exe).FileVersion!="3.12.1.0")throw new SetupFailure("vPilot version changed. Rebuild and verify plugin compatibility before installing.");
        settings=pairing.Settings(replies.Checked,disconnect.Checked);
        Installer.Install(Path.Combine(root,"release","FlightCompanion.Plugin.dll"),Path.Combine(installedRoot,"Plugins"),settings);
        installed=true;launch.Enabled=true;connect.Enabled=false;token.Enabled=false;replies.Enabled=false;disconnect.Enabled=false;install.Enabled=false;
        SetStatus("Installed and verified. The token is still only in this window's memory. Click Launch vPilot to pass it to vPilot, then send /status to the bot after a few seconds. Connect to VATSIM manually when ready.","installed");
      }catch(SetupFailure ex){SetStatus(ex.Message,"install-error");}
      catch {SetStatus("Could not install the plugin. Check file access; no token was saved.","install-error");}
    }
    void Launch() {
      try {
        if(!installed||client==null)throw new SetupFailure("Complete pairing and installation first.");
        if(Process.GetProcessesByName("vPilot").Length!=0)throw new SetupFailure("vPilot is already running. Setup will not stop or relaunch it.");
        using(var process=Process.Start(client.LaunchInfo(Path.Combine(installedRoot,"vPilot.exe"),settings.BotTokenEnvironmentVariable))) {
          if(process==null)throw new SetupFailure("vPilot did not start.");
        }
        SetStatus("vPilot launched with an in-memory token. Setup sent no VATSIM commands. Send /status to your bot after a few seconds.","vpilot-launched");
        Close();
      }catch(SetupFailure ex){SetStatus(ex.Message,"launch-error");}
      catch {SetStatus("vPilot could not be launched. No token was saved.","launch-error");}
    }
  }
  internal static class Program {
    [STAThread] static void Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SetupForm());}
  }
}
