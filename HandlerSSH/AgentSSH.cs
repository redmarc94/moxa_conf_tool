using System;
using System.Collections.Generic;
using System.Threading;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace HandlerSSH;

public class AgentSSH
{
    private readonly Logging loger;
    private readonly SshClient client;
    private ShellStream? shell;
    private readonly List<string> feedback;
    private bool stayconnected;
    private readonly ManualResetEvent disconnectEvent = new(false);
    public bool isconnected;

    public AgentSSH(string host, string username, string password)
    {
        loger = new Logging("AgentSSH");
        loger.Write($"Creazione oggetto AgentSSH per host {host} con user {username}");

        client = new SshClient(host, username, password)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(1)
        };
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);

        feedback = new List<string>();
    }

    public void Connect()
    {
        loger.Write($"Creazione Thread per la connessione verso {client.ConnectionInfo.Host}");
        stayconnected = true;
        disconnectEvent.Reset();
        Thread connectionThread = new(HandleConnection)
        {
            IsBackground = true
        };
        connectionThread.Start();
    }

    private void HandleConnection()
    {
        try
        {
            client.Connect();
            shell = client.CreateShellStream("ShellStream", 80, 24, 800, 600, 1024);
            loger.Write("Connessione SSH stabilita");

            Thread listener = new(() => ListenToShell(shell))
            {
                IsBackground = true
            };
            listener.Start();

            isconnected = true;
            loger.Write("Sessione SSH attiva, in attesa di disconnessione...");
            disconnectEvent.WaitOne();
        }
        catch (SshConnectionException ex)
        {
            loger.Write($"Errore di connessione SSH: {ex.Message}");
        }
        catch (SshAuthenticationException ex)
        {
            loger.Write($"Errore di autenticazione SSH: {ex.Message}");
        }
        catch (Exception ex)
        {
            loger.Write($"Eccezione durante la gestione della connessione: {ex.Message}");
        }
        finally
        {
            isconnected = false;
            try
            {
                if (client.IsConnected)
                {
                    client.Disconnect();
                }
            }
            catch (Exception ex)
            {
                loger.Write($"Errore durante la disconnessione: {ex.Message}");
            }

            shell?.Dispose();
            loger.Write("Connessione SSH chiusa");
        }
    }

    private void ListenToShell(ShellStream localShell)
    {
        try
        {
            while (stayconnected && localShell != null && localShell.CanRead)
            {
                if (!localShell.DataAvailable)
                {
                    Thread.Sleep(100);
                    continue;
                }

                string response = localShell.Read();
                string[] lines = response.Split('\n');
                lock (feedback)
                {
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Replace('\r', ' ').TrimEnd();
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        loger.Write(line);
                        feedback.Add(line);

                        if (line.Contains("Are you sure you want to enable a non-secure interface", StringComparison.OrdinalIgnoreCase))
                        {
                            localShell.WriteLine("y");
                            loger.Write("Risposta automatica 'y' inviata");
                        }
                    }
                }
            }
        }
        catch (ThreadInterruptedException)
        {
            // Ignora interruzioni esplicite del thread
        }
        catch (Exception ex)
        {
            loger.Write($"Errore durante la lettura dalla shell: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        stayconnected = false;
        disconnectEvent.Set();
    }

    private bool SendCommand(string command)
    {
        if (shell != null && client.IsConnected)
        {
            shell.Write(command + "\n");
            loger.Write("Ho eseguito: " + command);
            return true;
        }

        loger.Write("Impossibile inviare comando, shell non disponibile o client disconnesso");
        return false;
    }

    private bool SendCommandWaitPrompt(string command, int timeoutSec)
    {
        int startIndex;
        lock (feedback)
        {
            startIndex = feedback.Count;
        }

        if (!SendCommand(command))
        {
            return false;
        }

        DateTime start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < timeoutSec)
        {
            Thread.Sleep(500);
            if (!client.IsConnected)
            {
                loger.Write($"Connessione persa durante l'esecuzione di '{command}'");
                return false;
            }
            lock (feedback)
            {
                for (int i = startIndex; i < feedback.Count; i++)
                {
                    string line = feedback[i];
                    if (line.EndsWith("#", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    if (line.Contains("% Invalid input", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("not valid", StringComparison.OrdinalIgnoreCase))
                    {
                        loger.Write($"Errore durante l'esecuzione di '{command}': {line}");
                        return false;
                    }
                }
            }
        }

        loger.Write($"Timeout durante l'esecuzione del comando '{command}'");
        return false;
    }

    public bool ProgramMoxa(List<string> commands)
    {
        Thread.Sleep(2000);
        foreach (string cmd in commands)
        {
            bool success = SendCommandWaitPrompt(cmd, 180);
            if (!success)
            {
                loger.Write($"Interrompo sequenza comandi, '{cmd}' non è andato a buon fine.");
                return false;
            }
        }

        return true;
    }
}
