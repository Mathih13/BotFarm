using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BotFarm
{
    class RemoteAccess : IDisposable
    {
        private TcpClient client;
        private StreamReader reader;
        private StreamWriter writer;
        private string hostname;
        private int port;
        private string username;
        private string password;

        public RemoteAccess(string hostname, int port, string username, string password)
        {
            this.hostname = hostname;
            this.port = port;
            this.username = username;
            this.password = password;
        }

        public bool Connect()
        {
            try
            {
                Console.WriteLine($"RA: Attempting to connect to {hostname}:{port}");
                client = new TcpClient(hostname, port);
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 5000;
                reader = new StreamReader(stream, Encoding.UTF8);
                writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                // Wait for initial prompt
                Thread.Sleep(1000);
                
                // Read welcome message
                Console.WriteLine("RA: Reading welcome...");
                while (stream.DataAvailable)
                {
                    string line = reader.ReadLine();
                    Console.WriteLine($"RA: {line}");
                }

                // Send username
                Console.WriteLine($"RA: Sending username: {username}");
                writer.WriteLine(username);
                writer.Flush();
                Thread.Sleep(500);

                // Send password
                Console.WriteLine($"RA: Sending password");
                writer.WriteLine(password);
                writer.Flush();
                Thread.Sleep(1000);

                // Read authentication response and welcome
                Console.WriteLine("RA: Reading auth response...");
                bool authenticated = false;
                while (stream.DataAvailable)
                {
                    string line = reader.ReadLine();
                    Console.WriteLine($"RA: {line}");
                    if (line != null && line.Contains("Welcome"))
                        authenticated = true;
                    if (line != null && line.Contains("Authentication failed"))
                        authenticated = false;
                }

                Console.WriteLine($"RA: Connection {(authenticated ? "successful" : "assuming success")}");
                return true; // Return true even if we didn't see explicit success message
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RA connection failed: {ex.GetType().Name} - {ex.Message}");
                Console.WriteLine($"RA stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public string SendCommand(string command)
        {
            try
            {
                if (client == null || !client.Connected)
                {
                    if (!Connect())
                        return "Failed to connect";
                }

                Console.WriteLine($"RA: Sending command: {command}");
                writer.WriteLine(command);
                writer.Flush();
                
                // Wait longer for response - RA might be slow
                Thread.Sleep(1000);

                // Read response
                StringBuilder response = new StringBuilder();
                NetworkStream stream = client.GetStream();
                
                // Check multiple times for data
                for (int i = 0; i < 20; i++)
                {
                    if (stream.DataAvailable)
                    {
                        while (stream.DataAvailable)
                        {
                            string line = reader.ReadLine();
                            if (line != null)
                            {
                                response.AppendLine(line);
                                Console.WriteLine($"RA response: {line}");
                            }
                            Thread.Sleep(50);
                        }
                        break;
                    }
                    Thread.Sleep(100);
                }

                string result = response.ToString();
                if (string.IsNullOrEmpty(result))
                {
                    // No response might be normal for some commands
                    Console.WriteLine("RA: Command sent, no response (might be normal)");
                    return "Command sent";
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RA command failed: {ex.GetType().Name} - {ex.Message}");
                // Don't close connection on error, might be temporary
                return $"Error: {ex.Message}";
            }
        }

        public void Dispose()
        {
            reader?.Dispose();
            writer?.Dispose();
            client?.Dispose();
        }
    }
}
