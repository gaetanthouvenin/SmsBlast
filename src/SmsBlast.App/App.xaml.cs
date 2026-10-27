using System.Windows;

namespace SmsBlast.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Avant la création de la fenêtre : le bon dictionnaire de thème doit
        // déjà être en place quand MainWindow résout ses ressources.
        ThemeManager.Initialize();
        base.OnStartup(e);
    }
}
