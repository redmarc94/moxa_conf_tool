# Moxa Configuration Tool

Applicazione WPF per configurare automaticamente switch Moxa tramite sessioni SSH interattive.

## Funzionalità principali
- Connessione al vecchio IP di management e invio di comandi di configurazione iniziale.
- Salvataggio della configurazione e impostazione del nuovo indirizzo IP di management.
- Attesa del riavvio e verifica della raggiungibilità del nuovo IP tramite ping.
- Connessione al nuovo IP per completare la seconda fase di configurazione.
- Logging dettagliato su interfaccia grafica e file (rotazione giornaliera automatica).

## Struttura del progetto
- `App.xaml` / `App.xaml.cs`: entrypoint WPF.
- `MainWindow.xaml` / `MainWindow.xaml.cs`: interfaccia grafica e logica di alto livello.
- `HandlerSSH/Logging.cs`: logging su file e console con archiviazione giornaliera.
- `HandlerSSH/AgentSSH.cs`: gestione delle sessioni SSH tramite Renci.SshNet.
- `Properties/AssemblyInfo.cs`: configurazione temi WPF.
- `MoxaConfigApp.csproj`: progetto .NET 6 con WPF e dipendenza Renci.SshNet.

## Dipendenze
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) o superiore con supporto WPF (solo Windows).
- [Renci.SshNet](https://www.nuget.org/packages/Renci.SshNet/) (ultima versione disponibile).

## Esecuzione
1. Ripristinare i pacchetti NuGet: `dotnet restore`.
2. Compilare l'applicazione: `dotnet build`.
3. Avviare: `dotnet run`.

> **Nota:** L'applicazione è progettata per essere eseguita su Windows per il supporto WPF.

## Branch
- `github-branch`: ramo dedicato richiesto per l'integrazione su GitHub mantenendo i file di soluzione Visual Studio.
