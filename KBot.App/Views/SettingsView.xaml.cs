using System.Windows;
using System.Windows.Controls;
using KBot.App.ViewModels;

namespace KBot.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel profile, string section)
    {
        InitializeComponent();
        DataContext = profile;
        var all = section == "Settings";
        TargetsCard.Visibility = all || section == "Target" ? Visibility.Visible : Visibility.Collapsed;
        HealingCard.Visibility = all || section == "Healing" ? Visibility.Visible : Visibility.Collapsed;
        SpellsCard.Visibility = all || section == "Healing" ? Visibility.Visible : Visibility.Collapsed;
        AlertsCard.Visibility = all || section == "Alerts" ? Visibility.Visible : Visibility.Collapsed;
        FishingCard.Visibility = all || section == "Fishing" ? Visibility.Visible : Visibility.Collapsed;
        CatchCard.Visibility = all || section == "Catch" ? Visibility.Visible : Visibility.Collapsed;
        LootCard.Visibility = all || section == "Loot" ? Visibility.Visible : Visibility.Collapsed;
        ImportButton.Visibility = all ? Visibility.Visible : Visibility.Collapsed;
        SectionEyebrow.Text = all ? "PERFIL DE AUTOMAÇÃO" : "CONFIGURAÇÃO DO MÓDULO";
        SectionHeading.Text = section switch
        {
            "Target" => "Organize a prioridade das criaturas.",
            "Healing" => "Prepare revive, comida e magias.",
            "Alerts" => "Configure alertas e atalhos manuais.",
            "Fishing" => "Defina o ponto e o atalho de pesca.",
            "Catch" => "Prepare as opções de captura.",
            "Loot" => "Prepare as opções de coleta.",
            _ => "Configure os recursos do KBot em um perfil local."
        };
    }
}
