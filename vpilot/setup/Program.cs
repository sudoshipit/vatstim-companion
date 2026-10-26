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
[assembly:AssemblyVersion("0.2.3.0")]
[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
[assembly:InternalsVisibleTo("FlightCompanion.Setup.Tests")]
namespace FlightCompanionSetup {
  internal sealed class SetupForm : Form {
    readonly TextBox token=new TextBox(),pairCode=new TextBox(),status=new TextBox(),location=new TextBox();
    readonly Label instructions=new Label(),locationStatus=new Label();
    readonly Button connect=new Button(),install=new Button(),launch=new Button(),browse=new Button(),openBot=new Button();
    readonly CheckBox replies=new CheckBox(),disconnect=new CheckBox();
    readonly CancellationTokenSource cancel=new CancellationTokenSource();
    string installedRoot,botLink;
    BotClient client;Pairing pairing;bool installed,busy;
    internal SetupForm() {
      Text="Flight Companion Setup";ClientSize=new Size(650,650);StartPosition=FormStartPosition.CenterScreen;
      Font=new Font("Segoe UI",10);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;AutoScroll=true;
      AddLabel("1. Choose your vPilot installation",18,15,610,25);
      location.SetBounds(18,43,490,30);location.AccessibleName="vPilot installation path";Controls.Add(location);
      browse.SetBounds(520,40,108,35);browse.Text="Browse...";browse.Click+=(s,e)=>Browse();Controls.Add(browse);
      locationStatus.SetBounds(18,80,610,40);Controls.Add(locationStatus);
      location.TextChanged+=(s,e)=>CheckLocation();
      location.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"vPilot");
      AddLabel("2. Connect your Telegram bot",18,128,390,28);
      var createBot=new LinkLabel{Text="Create a bot",AutoSize=true};createBot.Location=new Point(520,130);createBot.LinkClicked+=(s,e)=>OpenLink("https://t.me/BotFather");Controls.Add(createBot);
      token.SetBounds(18,160,610,30);token.UseSystemPasswordChar=true;token.AccessibleName="Telegram bot token";Controls.Add(token);
      connect.Text="Connect Telegram";connect.SetBounds(18,202,190,35);connect.Click+=async(s,e)=>await Pair();Controls.Add(connect);
      AddLabel("Your token will be saved encrypted for your Windows account.",18,244,610,28);
      instructions.SetBounds(18,284,610,45);instructions.Text="Paste the bot token from BotFather, then click Connect Telegram.";Controls.Add(instructions);
      openBot.SetBounds(18,333,175,35);openBot.Text="Open Telegram";openBot.Enabled=false;openBot.Click+=(s,e)=>OpenLink(botLink);Controls.Add(openBot);
      pairCode.SetBounds(210,336,418,30);pairCode.ReadOnly=true;pairCode.AccessibleName="Alternative pairing code";Controls.Add(pairCode);
      replies.SetBounds(18,389,610,28);replies.Text="Allow replies to recent private vPilot messages";Controls.Add(replies);
      disconnect.SetBounds(18,421,610,28);disconnect.Text="Allow remote disconnect (requires /disconnect, then /confirm)";Controls.Add(disconnect);
      install.SetBounds(18,466,280,38);install.Text="3. Install Flight Companion";install.Enabled=false;install.Click+=(s,e)=>Install();Controls.Add(install);
      launch.SetBounds(316,466,280,38);launch.Text="Open vPilot";launch.Enabled=false;launch.Click+=(s,e)=>Launch();Controls.Add(launch);
      status.SetBounds(18,524,610,101);status.Multiline=true;status.ReadOnly=true;status.ScrollBars=ScrollBars.Vertical;Controls.Add(status);
      SetStatus("One-time setup. Start vPilot normally after installation; no configuration editing is needed. Close vPilot before connecting Telegram or installing.");
      FormClosed+=(s,e)=>{cancel.Cancel();if(client!=null)client.Dispose();token.Clear();};
    }
    void Browse() {
      using(var picker=new OpenFileDialog {Title="Select your installed vPilot.exe",Filter="vPilot application (vPilot.exe)|vPilot.exe",CheckFileExists=true,Multiselect=false,RestoreDirectory=true}) {
        string candidate=Environment.ExpandEnvironmentVariables(location.Text.Trim().Trim('"'));
        if(Directory.Exists(candidate))picker.InitialDirectory=candidate;
        else if(File.Exists(candidate))picker.InitialDirectory=Path.GetDirectoryName(candidate);
        if(picker.ShowDialog(this)==DialogResult.OK)location.Text=picker.FileName;
      }
    }
    void CheckLocation() {
      bool valid=false;
      try {VpilotLocation.Validate(location.Text);valid=true;locationStatus.Text="vPilot 3.12.1 found. Other drives are supported through Browse.";locationStatus.ForeColor=Color.DarkGreen;}
      catch(SetupFailure ex){locationStatus.Text=ex.Message;locationStatus.ForeColor=Color.Maroon;}
      catch {locationStatus.Text="Could not read this installation. Check access, or choose vPilot.exe using Browse.";locationStatus.ForeColor=Color.Maroon;}
      install.Enabled=valid&&!busy&&!installed&&pairing!=null&&pairing.ChatId>0;
    }
    void OpenLink(string url) {
      if(String.IsNullOrEmpty(url))return;
      try {Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}
      catch {SetStatus("Could not open Telegram. Open your bot's private chat and send the pairing code shown above.");}
    }
    void AddLabel(string text,int x,int y,int w,int h){var label=new Label{Text=text};label.SetBounds(x,y,w,h);Controls.Add(label);}
    void SetStatus(string text){if(!IsDisposed)status.Text=text;}
    static void RequireStopped() {
      var processes=Process.GetProcessesByName("vPilot");bool running=processes.Length!=0;
      foreach(var process in processes)process.Dispose();
      if(running)throw new SetupFailure("Close vPilot when convenient, then try again. Setup will not stop it or interrupt a flight.");
    }
    static long Now(){return (long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;}
    async Task Pair() {
      if(busy||installed)return;
      try {RequireStopped();VpilotLocation.Validate(location.Text);}catch(SetupFailure ex){SetStatus(ex.Message);return;}
      catch {SetStatus("Check the selected vPilot installation and try again.");return;}
      busy=true;connect.Enabled=false;install.Enabled=false;launch.Enabled=false;openBot.Enabled=false;botLink=null;
      if(client!=null){client.Dispose();client=null;}pairing=null;pairCode.Clear();
      try {
        string supplied=token.Text.Trim();token.Clear();client=new BotClient(supplied);supplied=null;
        SetStatus("Checking your Telegram bot...");
        string username=await client.Verify(cancel.Token);
        pairing=new Pairing(Now());pairCode.Text=pairing.Code;botLink=pairing.Link(username);openBot.Enabled=true;
        instructions.Text="Click Open Telegram, then press Start in @"+username+". Alternatively, send the code below to that bot in a private chat.";
        SetStatus("Waiting for you to press Start in Telegram. This pairing link expires in five minutes.");
        while(!cancel.IsCancellationRequested&&Now()-pairing.Started<=300) {
          RequireStopped();
          object updates=await client.Call("getUpdates",new {offset=pairing.Offset,timeout=15,limit=100,allowed_updates=new[]{"message"}},cancel.Token);
          if(pairing.Accept(updates,Now())) {
            pairCode.Text="Telegram connected";openBot.Enabled=false;
            instructions.Text="Telegram connected. Choose the optional controls below, then install.";
            SetStatus("Ready to install. The plugin is included in this setup file. Your token will be encrypted and remembered for normal vPilot launches.");return;
          }
          await Task.Delay(300,cancel.Token);
        }
        throw new SetupFailure("Pairing expired. Paste the token and click Connect Telegram to try again.");
      }catch(OperationCanceledException){SetStatus("Setup cancelled or request timed out. Your token has not been saved.");}
      catch(SetupFailure ex){SetStatus(ex.Message);}
      catch {SetStatus("Could not connect Telegram. Check your connection and try again.");}
      finally {busy=false;if(!IsDisposed){connect.Enabled=true;openBot.Enabled=false;CheckLocation();}}
    }
    void Install() {
      try {
        if(installed||pairing==null||pairing.ChatId<=0||client==null)throw new SetupFailure("Connect Telegram first.");
        RequireStopped();installedRoot=VpilotLocation.Validate(location.Text);
        var settings=pairing.Settings(replies.Checked,disconnect.Checked);client.SaveToken(settings);
        Installer.InstallEmbedded(Path.Combine(installedRoot,"Plugins"),settings);
        installed=true;launch.Enabled=true;connect.Enabled=false;token.Enabled=false;replies.Enabled=false;disconnect.Enabled=false;install.Enabled=false;location.Enabled=false;browse.Enabled=false;
        client.Dispose();client=null;
        SetStatus("Installed in "+Path.Combine(installedRoot,"Plugins")+". Your token is saved encrypted. Open vPilot here or use your usual shortcut. Send /status to your bot after startup, then connect to VATSIM manually.");
      }catch(SetupFailure ex){SetStatus(ex.Message);}
      catch {SetStatus("Could not install. Check access to the selected folder and try again.");}
    }
    void Launch() {
      try {
        if(!installed)throw new SetupFailure("Complete installation first.");
        RequireStopped();VpilotLocation.Validate(installedRoot);
        using(var process=Process.Start(VpilotLocation.LaunchInfo(installedRoot))) {
          if(process==null)throw new SetupFailure("vPilot did not start.");
        }
        Close();
      }catch(SetupFailure ex){SetStatus(ex.Message);}
      catch {SetStatus("vPilot could not be opened. You can start it using your usual shortcut.");}
    }
  }
  internal static class Program {
    [STAThread] static void Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SetupForm());}
  }
}
