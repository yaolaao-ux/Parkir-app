namespace Parkir.Pos;

public partial class App : System.Windows.Application
{
	protected override void OnStartup(System.Windows.StartupEventArgs e)
	{
		base.OnStartup(e);
		var loginWindow = new LoginWindow();
		if (loginWindow.ShowDialog() != true || loginWindow.Session is null)
		{
			Shutdown();
			return;
		}

		var mainWindow = new MainWindow(loginWindow.Session);
		MainWindow = mainWindow;
		mainWindow.Show();
	}
}