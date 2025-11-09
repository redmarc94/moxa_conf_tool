using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows;
using HandlerSSH;

namespace MoxaConfigApp;

public partial class MainWindow : Window
{
    private readonly Logging logger;

    public MainWindow()
    {
        InitializeComponent();
        logger = new Logging("MoxaConfigApp");
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

        var firstPhaseCommands = new List<string>
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
            "copy running startup",
            "conf t",
            $"ip management address {ipAddress} 255.255.240.0",
            "exit"
        };

        var secondPhaseCommands = new List<string>
        {
            "copy running startup"
        };

        try
        {
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
                        Log("Impossibile stabilire la connessione SSH al vecchio IP entro il tempo previsto. Ritento...");
                        Thread.Sleep(5000);
                        continue;
                    }

                    Log("Connessione stabilita con il vecchio IP, invio comandi prima fase...");
                    bool phaseSuccess = agentOld.ProgramMoxa(firstPhaseCommands);

                    if (phaseSuccess)
                    {
                        Log("Prima fase completata correttamente sul vecchio IP.");
                        firstPhaseCompleted = true;
                    }
                    else
                    {
                        Log("La prima fase non è andata a buon fine (possibile disconnessione). Ritento l'intera sequenza...");
                        Thread.Sleep(5000);
                    }
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
