using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using HandlerSSH;

namespace MoxaConfigApp;

public partial class MainWindow : Window
{
    private sealed class NetworkInterfaceItem
    {
        public NetworkInterfaceItem(NetworkInterface networkInterface)
        {
            Interface = networkInterface;
            Name = networkInterface.Name;
            Description = networkInterface.Description;
            Status = networkInterface.OperationalStatus;
        }

        public string Name { get; }
        public string Description { get; }
        public OperationalStatus Status { get; }
        public NetworkInterface Interface { get; }
        public string Display => $"{Name} ({Description}) - {Status}";
    }

    private const string DefaultSwitchIp = "192.168.127.254";
    private const string DefaultLocalManagementIp = "192.168.127.253";
    private static readonly IPAddress ManagementNetwork = IPAddress.Parse("192.168.127.0");
    private static readonly IPAddress ManagementMask = IPAddress.Parse("255.255.255.0");

    private readonly Logging logger;

    public MainWindow()
    {
        EnsureAdminPrivileges();

        InitializeComponent();
        logger = new Logging("MoxaConfigApp");

        txtSSHHost.Text = DefaultSwitchIp;
        txtIPAddress.Text = DefaultSwitchIp;

        LoadNetworkInterfaces();
    }

    private void btnConfigure_Click(object sender, RoutedEventArgs e)
    {
        txtLog.Clear();
        Log("Inizio procedura di configurazione...");

        string montante = txtMontante.Text.Trim();
        string ipAddress = txtIPAddress.Text.Trim();
        string oldHost = txtSSHHost.Text.Trim();

        if (string.IsNullOrWhiteSpace(montante) || string.IsNullOrWhiteSpace(ipAddress) || string.IsNullOrWhiteSpace(oldHost))
        {
            MessageBox.Show("Inserire Montante, Indirizzo IP e SSH Host");
            return;
        }

        string newHost = ipAddress;

        var baseCommands = new List<string>
        {
            "conf t",
            $"hostname {montante}",
            $"lldp chassis-id-subtype local {montante}",
            "spanning-tree errordisable recovery-interval 30",
            "spanning-tree max-age 6",
            "spanning-tree hello-time 1",
            "spanning-tree forward-time 4",
            "spanning-tree priority 32768",
            "rstp enable",
            "ptp profile c37.238 mode transparent delay-mechanism p2p",
            "ptp profile c37.238 domain 0",
            "ptp enable",
            "clock source ptp",
            "clock timezone 1",
            "snmp community read-write public",
            "snmp-server version v1-v2c",
            "snmp-server access enable",
            "interface ethernet 1/1-4",
            "spanning-tree",
            "ptp profile c37.238",
            "exit",
            "interface ethernet 2/1-8",
            "spanning-tree",
            "ptp profile c37.238",
            "exit",
            "interface ethernet 3/1-8",
            "spanning-tree",
            "ptp profile c37.238",
            "exit",
            "interface ethernet 4/1-8",
            "spanning-tree",
            "ptp profile c37.238",
            "exit",
            "exit",
            "copy running startup"
        };

        var secondPhaseCommands = new List<string> { "copy running startup" };

        try
        {
            if (!EnsureLocalManagementIp())
            {
                return;
            }

            if (!CheckInitialReachability(oldHost))
            {
                MessageBox.Show($"Nessuna risposta da {oldHost}. Controllare collegamento e riprovare.");
                return;
            }

            const int maxFirstPhaseAttempts = 5;
            bool firstPhaseCompleted = false;

            for (int attempt = 1; attempt <= maxFirstPhaseAttempts && !firstPhaseCompleted; attempt++)
            {
                Log($"Tentativo {attempt} di connessione SSH a {oldHost} con user admin...");
                AgentSSH agentOld = new(oldHost, "admin", "moxa");
                agentOld.Connect();

                try
                {
                    bool connected = WaitForSshConnection(agentOld, 30);
                    if (!connected)
                    {
                        Log("Impossibile stabilire la connessione SSH al vecchio IP entro il tempo previsto. Ritento procedura completa...");
                        Thread.Sleep(5000);
                        continue;
                    }

                    Log("Connessione stabilita con il vecchio IP, invio comandi prima fase...");
                    bool baseCommandsOk = agentOld.ProgramMoxa(baseCommands);

                    if (!baseCommandsOk || !agentOld.isconnected)
                    {
                        Log("Disconnessione o errore durante i comandi iniziali. Riavvio la sequenza dall'inizio...");
                        Thread.Sleep(5000);
                        continue;
                    }

                    Log("Controllo sessione SSH prima del cambio IP...");
                    if (!agentOld.isconnected)
                    {
                        Log("Sessione SSH non più attiva prima del cambio IP. Riavvio procedura...");

                    if (!baseCommandsOk || !agentOld.isconnected)
                    {
                        Log("Disconnessione o errore durante i comandi iniziali. Riavvio la sequenza dall'inizio...");
                        Thread.Sleep(5000);
                        continue;
                    }

                    bool enteredConf = agentOld.ProgramMoxa(new List<string> { "conf t" });
                    if (!enteredConf || !agentOld.isconnected)
                    {
                        Log("Impossibile rientrare in configurazione prima del cambio IP. Riavvio procedura...");
                    Log("Controllo sessione SSH prima del cambio IP...");
                    if (!agentOld.isconnected)
                    {
                        Log("Sessione SSH non più attiva prima del cambio IP. Riavvio procedura...");
                        Thread.Sleep(5000);
                        continue;
                    }

                    bool enteredConf = agentOld.ProgramMoxa(new List<string> { "conf t" });
                    if (!enteredConf || !agentOld.isconnected)
                    {
                        Log("Impossibile rientrare in configurazione prima del cambio IP. Riavvio procedura...");
                        Thread.Sleep(5000);
                        continue;
                    }

                    Log($"Invio comando di cambio IP ({ipAddress}) e attendo disconnessione...");
                    bool ipChangeSent = agentOld.SendCommandExpectDisconnect($"ip management address {ipAddress} 255.255.240.0", 20);

                    if (!ipChangeSent)
                    {
                        Log("Il comando di cambio IP non è stato confermato. Riavvio procedura...");
                        Thread.Sleep(5000);
                        continue;
                    }

                    Log("Prima fase completata correttamente sul vecchio IP. Attesa per riavvio...");
                    firstPhaseCompleted = true;
                }
                finally
                {
                    Log("Disconnessione dal vecchio IP in corso...");
                    agentOld.Disconnect();
                }
            }

            if (!firstPhaseCompleted)
            {
                Log("Impossibile completare la prima fase dopo diversi tentativi. Interrompo la procedura.");
                MessageBox.Show("Connessione instabile con il vecchio IP. Verificare lo switch e riprovare.");
                return;
            }

            Log("Attesa di 60 secondi per il riavvio del switch con il nuovo IP...");
            Thread.Sleep(60000);

            Log($"Tentativo di ping verso il nuovo IP: {newHost}...");
            if (WaitForPing(newHost, 60))
            {
                Log("Il nuovo IP risponde al ping, procedo con la connessione SSH.");
                AgentSSH agentNew = new(newHost, "admin", "moxa");
                agentNew.Connect();

                try
                {
                    if (!WaitForSshConnection(agentNew, 30))
                    {
                        Log("Impossibile stabilire la connessione SSH con il nuovo IP. Interrompo la procedura.");
                        MessageBox.Show("Connessione SSH al nuovo IP non disponibile. Verificare la configurazione.");
                        return;
                    }

                    Log("Connessione stabilita con il nuovo IP, invio comandi seconda fase...");
                    bool secondPhaseOk = agentNew.ProgramMoxa(secondPhaseCommands);

                    if (!secondPhaseOk)
                    {
                        Log("La seconda fase non è stata completata correttamente. Verificare lo stato dello switch.");
                        MessageBox.Show("La configurazione sul nuovo IP potrebbe non essere completa. Controllare i log.");
                        return;
                    }

                    Log("Configurazione con il nuovo IP completata con successo!");
                    MessageBox.Show("Configurazione completata con successo!");
                }
                finally
                {
                    Log("Disconnessione dal nuovo IP in corso...");
                    agentNew.Disconnect();
                }
            }
            else
            {
                Log("Il nuovo IP non risponde al ping dopo 60 secondi, interrompo la procedura.");
                MessageBox.Show("Il nuovo IP non è raggiungibile. Verificare la configurazione.");
            }
        }
        catch (Exception ex)
        {
            Log($"Errore durante la configurazione: {ex.Message}");
            MessageBox.Show("Errore durante la configurazione. Controllare i log per maggiori dettagli.");
        }
    }

    private bool WaitForPing(string host, int timeoutSeconds)
    {
        using Ping pinger = new();
        DateTime start = DateTime.Now;

        while ((DateTime.Now - start).TotalSeconds < timeoutSeconds)
        {
            try
            {
                PingReply? reply = pinger.Send(host, 2000);
                if (reply is { Status: IPStatus.Success })
                {
                    return true;
                }
            }
            catch
            {
                // Ignora eccezioni del ping (IP non raggiungibile, ecc.)
            }

            Thread.Sleep(2000);
        }

        return false;
    }

    private bool CheckInitialReachability(string host)
    {
        Log($"Verifico raggiungibilità iniziale di {host}...");
        bool reachable = WaitForPing(host, 20);
        if (!reachable)
        {
            Log($"Nessuna risposta da {host}. Assicurarsi di essere collegati alla rete 192.168.127.0/24.");
        }
        else
        {
            Log($"Ping a {host} riuscito. Procedo con la connessione SSH.");
        }

        return reachable;
    }

    private bool EnsureLocalManagementIp()
    {
        try
        {
            Log("Verifica presenza IP locale nel range 192.168.127.0/24...");

            IEnumerable<(NetworkInterface Interface, IPAddress Address)> addresses = NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback && ni.OperationalStatus == OperationalStatus.Up)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses
                    .Where(ip => ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(ip => (Interface: ni, Address: ip.Address)));

            foreach ((NetworkInterface Interface, IPAddress Address) info in addresses)
            {
                if (IsInSameSubnet(info.Address, ManagementNetwork, ManagementMask))
                {
                    Log($"IP {info.Address} già configurato sull'interfaccia '{info.Interface.Name}'.");
                    return true;
                }
            }

            NetworkInterface? targetInterface = GetSelectedInterface();

            if (targetInterface == null)
            {
                Log("Nessuna interfaccia valida selezionata per aggiungere l'indirizzo di management.");
                MessageBox.Show("Seleziona una scheda di rete per aggiungere l'IP 192.168.127.x");
                return false;
            }

            Log($"Interfaccia scelta per aggiungere l'IP locale: {targetInterface.Name} - {targetInterface.Description}");

            NetworkInterface? targetInterface = NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(ni => ni.OperationalStatus == OperationalStatus.Up)
                .ThenByDescending(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                .FirstOrDefault();

            if (targetInterface == null)
            {
                Log("Nessuna interfaccia valida trovata per aggiungere l'indirizzo di management.");
                MessageBox.Show("Impossibile trovare un'interfaccia di rete per aggiungere l'IP 192.168.127.x");
                return false;
            }

            return AddLocalManagementIp(targetInterface);
        }
        catch (Exception ex)
        {
            Log($"Errore durante la verifica o aggiunta dell'IP locale: {ex.Message}");
            MessageBox.Show("Errore durante la configurazione dell'IP locale. Controllare i log.");
            return false;
        }
    }

    private bool AddLocalManagementIp(NetworkInterface targetInterface)
    {
        string interfaceName = targetInterface.Name;
        Log($"Aggiungo IP {DefaultLocalManagementIp} su '{interfaceName}'...");

        ProcessStartInfo psi = new()
        {
            FileName = "netsh",
            Arguments = $"interface ipv4 add address \"{interfaceName}\" {DefaultLocalManagementIp} 255.255.255.0 store=active",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process proc = new() { StartInfo = psi };
        proc.Start();
        string output = proc.StandardOutput.ReadToEnd();
        string error = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode == 0)
        {
            Log($"IP aggiunto correttamente. Output: {output.Trim()}");
            return true;
        }

        Log($"Impossibile aggiungere IP su '{interfaceName}'. Errore: {error.Trim()} {output.Trim()}");
        MessageBox.Show("Non è stato possibile aggiungere l'IP locale. Avviare l'applicazione come amministratore.");
        return false;
    }

    private static bool IsInSameSubnet(IPAddress address, IPAddress network, IPAddress mask)
    {
        byte[] ipBytes = address.GetAddressBytes();
        byte[] networkBytes = network.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();

        for (int i = 0; i < ipBytes.Length; i++)
        {
            if ((ipBytes[i] & maskBytes[i]) != (networkBytes[i] & maskBytes[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool WaitForSshConnection(AgentSSH agent, int timeoutSeconds)
    {
        DateTime start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < timeoutSeconds)
        {
            if (agent.isconnected)
            {
                return true;
            }

            Thread.Sleep(200);
        }

        return agent.isconnected;
    }

    private void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            txtLog.ScrollToEnd();
        });

            logger.Write(message);
        }
    }

    private void LoadNetworkInterfaces()
    {
        IEnumerable<NetworkInterfaceItem> interfaces = NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback && ni.Supports(NetworkInterfaceComponent.IPv4))
            .OrderByDescending(ni => ni.OperationalStatus == OperationalStatus.Up)
            .ThenByDescending(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            .ThenBy(ni => ni.Name)
            .Select(ni => new NetworkInterfaceItem(ni));

        cmbInterfaces.ItemsSource = interfaces.ToList();

        if (cmbInterfaces.Items.Count > 0)
        {
            cmbInterfaces.SelectedIndex = 0;
            Log($"Interfaccia predefinita selezionata: {(cmbInterfaces.SelectedItem as NetworkInterfaceItem)?.Display}");
        }
        else
        {
            MessageBox.Show("Nessuna interfaccia di rete disponibile per configurare l'IP locale.");
            Log("Nessuna interfaccia di rete trovata.");
        }
    }

    private NetworkInterface? GetSelectedInterface()
    {
        if (cmbInterfaces.SelectedItem is NetworkInterfaceItem item)
        {
            return item.Interface;
        }

        if (cmbInterfaces.Items.Count > 0)
        {
            cmbInterfaces.SelectedIndex = 0;
            return (cmbInterfaces.SelectedItem as NetworkInterfaceItem)?.Interface;
        }

        return null;
    }

    private void EnsureAdminPrivileges()
    {
        WindowsIdentity? identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);

        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            return;
        }

        try
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            exePath ??= Assembly.GetEntryAssembly()?.Location ?? Assembly.GetExecutingAssembly().Location;

            ProcessStartInfo psi = new()
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(arg => $"\"{arg}\""))
            };

            Process.Start(psi);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Per configurare l'IP locale servono i diritti di amministratore. Chiudere e riavviare come amministratore. Dettagli: {ex.Message}");
            Application.Current.Shutdown();
        }
    }
