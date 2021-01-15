using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace FTPServer
{
    class Program
    {
        static void Main(string[] args)
        {
            FileTransfer.FTServer fTServer = new FileTransfer.FTServer(3000);

        }
    }
    class FileTransfer
    {
        public class FTClient
        {
            private string IP;
            private int ServerPort;
            private int ClientPort;
            private Thread lectordepaquetes;
            private Thread ReconectarThread;

            Socket sock;

            private int TasaDeActualizacionDePaquetes = 100;
            private bool EstaConectado = false;
            private bool EsperandoRespuesta = false;
            private bool Reconectando = false;
            private int MAX_INTENTOS = 10;
            private int intentos = 0;

            public event EventHandler<OnConnectionStartEventArgs> OnConnectionStart;
            public event EventHandler<EventArgs> OnConnectionSuccesful;
            public event EventHandler<EventArgs> OnConnectionFinished;
            public event EventHandler<OnConnectionFailedEventArgs> OnConnectionFailed;
            public event EventHandler<OnFileLineReceivedArgs> OnFileLineReceived;

            public event EventHandler<OnFileDownloadStartArgs> OnFileDownloadStart;
            public event EventHandler<OnFileDownloadFinishArgs> OnFileDownloadFinish;
            public event EventHandler<OnErrorReceivedEventArgs> OnErrorReceived;
            public event EventHandler<OnUnknownPackageReceivedEventArgs> OnUnknownPackageReceived;

            public event EventHandler<OnDataArrivesEventArgs> OnDataArrives;

            private List<string> ParametrosRespuesta = new List<string>();

            public FTClient(string ip, int port, int connectionattemps = 10)
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                lectordepaquetes = new Thread(LectorDePaquetes);
                lectordepaquetes.Start();
                this.MAX_INTENTOS = connectionattemps;
                Conectar(ip, port);
            }

            public bool IsConnected()
            {
                return EstaConectado;
            }

            public List<string> RetrieveFilesInFolder(string Folder)
            {
                Paquete p = new Paquete(Paquete.Paquetes.SOLICITAR_ARCHIVOS_DIRECTORIO, StringToStringList(Folder));
                p.Enviar(IP, ServerPort);
                EsperarHastaObtenerRespuesta();
                return ObtenerParametrosDeRespuesta();
            }

            public string GetSpecialFolder(Paquete.CARPETAS_ESPECIALES folder)
            {
                Paquete p = new Paquete(Paquete.Paquetes.SOLICITAR_CARPETA_ESPECIAL, StringToStringList(folder.ToString()));
                p.Enviar(IP, ServerPort);
                EsperarHastaObtenerRespuesta();
                return ObtenerParametrosDeRespuesta().ElementAt(0);
            }

            public void DownloadFile(string file)
            {
                Paquete p = new Paquete(Paquete.Paquetes.SOLICITAR_ARCHIVO, StringToStringList(file));
                p.Enviar(IP, ServerPort);
                EsperarHastaObtenerRespuesta();
            }
            public void DownloadFolder(string Folder)
            {
                List<string> files = RetrieveFilesInFolder(Folder);
                foreach (string file in files)
                {
                    DownloadFile(file);
                }
            }
            public List<string> ObtenerParametrosDeRespuesta()
            {
                return ParametrosRespuesta;
            }

            private void Conectar(string ip, int port)
            {
                this.IP = ip;
                this.ServerPort = port;
                this.ClientPort = port + 1;
                OnConnectionStart?.Invoke(this, new OnConnectionStartEventArgs(this.IP, this.ServerPort));
                Paquete p = new Paquete(Paquete.Paquetes.INICIO_COMUNICACION);
                p.Enviar(this.IP, this.ServerPort);
                Thread.Sleep(TasaDeActualizacionDePaquetes * 3);
                if (EstaConectado == false)
                {
                    Reconnect();
                }
            }
            private bool running = true;
            private void LectorDePaquetes()
            {
                UdpClient receivingUdpClient = new UdpClient(ClientPort);
                IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
                while (running)
                {
                    try
                    {
                        Byte[] receiveBytes = receivingUdpClient.Receive(ref RemoteIpEndPoint);
                        string returnData = Encoding.ASCII.GetString(receiveBytes);
                        Paquete paquete = new Paquete(returnData);
                        OnDataArrives?.Invoke(this, new OnDataArrivesEventArgs(returnData));
                        if (paquete.Tipo != Paquete.Paquetes.PAQUETE_DESCONOCIDO && EsperandoRespuesta)
                        {
                            EsperandoRespuesta = false;
                            ParametrosRespuesta = paquete.parametros;
                        }
                        switch (paquete.Tipo)
                        {
                            case Paquete.Paquetes.CONFIRMAR_INICIO_COMUNICACION:
                                {
                                    OnConnectionSuccesful?.Invoke(this, new EventArgs());
                                    EstaConectado = true;
                                    Reconectando = false;
                                    break;
                                }
                            case Paquete.Paquetes.FIN_COMUNICACION:
                                {
                                    OnConnectionFinished?.Invoke(this, new EventArgs());
                                    EstaConectado = false;
                                    break;
                                }
                            case Paquete.Paquetes.CONFIRMAR_SOLICITUD_ARCHIVO:
                                {
                                    TasaDeActualizacionDePaquetes = TasaDeActualizacionDePaquetes / 20;
                                    OnFileDownloadStart?.Invoke(this, new OnFileDownloadStartArgs(paquete.parametros.ElementAt(0)));
                                    break;
                                }
                            case Paquete.Paquetes.FIN_DE_ARCHIVO:
                                {
                                    TasaDeActualizacionDePaquetes = TasaDeActualizacionDePaquetes * 20;
                                    OnFileDownloadFinish?.Invoke(this, new OnFileDownloadFinishArgs(paquete.parametros.ElementAt(0)));
                                    break;
                                }
                            case Paquete.Paquetes.CONTENIDO_ARCHIVO:
                                {
                                    OnFileLineReceived?.Invoke(this, new OnFileLineReceivedArgs(paquete.parametros.ElementAt(0)));
                                    break;
                                }
                            case Paquete.Paquetes.RESPUESTA_SOLICITUD_ARCHIVOS_DIRECTORIO:
                                {
                                    break;
                                }
                            case Paquete.Paquetes.RESPUESTA_SOLICITUD_CARPETA_ESPECIAL:
                                {
                                    break;
                                }
                            case Paquete.Paquetes.ARCHIVO_INEXISTENTE:
                                {
                                    OnErrorReceived?.Invoke(this, new OnErrorReceivedEventArgs(paquete.Tipo, paquete.parametros.ElementAt(0)));
                                    break;
                                }
                            case Paquete.Paquetes.CARPETA_INEXISTENTE:
                                {
                                    OnErrorReceived?.Invoke(this, new OnErrorReceivedEventArgs(paquete.Tipo, paquete.parametros.ElementAt(0)));
                                    break;
                                }
                            default:
                                {
                                    paquete.Tipo = Paquete.Paquetes.PAQUETE_DESCONOCIDO;
                                    OnUnknownPackageReceived?.Invoke(this, new OnUnknownPackageReceivedEventArgs(returnData));
                                    break;
                                }
                        }
                        Thread.Sleep(TasaDeActualizacionDePaquetes);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message.ToString());
                    }
                }
            }

            public void Reconnect()
            {
                ReconectarThread = new Thread(Reconectar);
                ReconectarThread.Start();
            }

            private void Reconectar()
            {
                Reconectando = true;
                while (Reconectando && EstaConectado == false)
                {
                    if (intentos < MAX_INTENTOS)
                    {
                        OnConnectionStart?.Invoke(this, new OnConnectionStartEventArgs(this.IP, this.ServerPort));
                        Paquete p = new Paquete(Paquete.Paquetes.INICIO_COMUNICACION);
                        p.Enviar(this.IP, this.ServerPort);
                        intentos++;
                        Thread.Sleep(TasaDeActualizacionDePaquetes * 30);
                    }
                    else
                    {
                        OnConnectionFailed?.Invoke(this, new OnConnectionFailedEventArgs(this.IP, this.ServerPort));
                        break;
                    }
                }
            }

            public void Disconnect()
            {
                Paquete p = new Paquete(Paquete.Paquetes.FIN_COMUNICACION);
                p.Enviar(this.IP, this.ServerPort);
                if (ReconectarThread.IsAlive == true)
                {
                    Reconectando = false;
                    EstaConectado = true;
                    ReconectarThread.Abort();
                }
                if (lectordepaquetes.IsAlive == true)
                {
                    running = false;
                    lectordepaquetes.Abort();
                }
                if (EsperandoRespuesta == true)
                {
                    EsperandoRespuesta = false;
                }
                sock.Close();
            }

            private void EsperarHastaObtenerRespuesta()
            {
                EsperandoRespuesta = true;
                while (EsperandoRespuesta)
                {
                    Thread.Sleep(10);
                }
            }

            public class OnFileLineReceivedArgs : EventArgs
            {
                public readonly string Line;

                public OnFileLineReceivedArgs(string LineContent)
                {
                    this.Line = LineContent;
                }
            }

            public class OnFileDownloadStartArgs : EventArgs
            {
                public readonly string file;

                public OnFileDownloadStartArgs(string file)
                {
                    this.file = file;
                }
            }

            public class OnFileDownloadFinishArgs : EventArgs
            {
                public readonly string File;

                public OnFileDownloadFinishArgs(string File)
                {
                    this.File = File;
                }
            }
            public class OnConnectionFailedEventArgs : EventArgs
            {
                public readonly string IP;
                public readonly int port;
                public OnConnectionFailedEventArgs(string IP, int port)
                {
                    this.IP = IP;
                    this.port = port;
                }
            }
            public class OnUnknownPackageReceivedEventArgs : EventArgs
            {
                public readonly string Package;
                public OnUnknownPackageReceivedEventArgs(string package)
                {
                    this.Package = package;
                }
            }
            public class OnErrorReceivedEventArgs : EventArgs
            {
                public readonly Paquete.Paquetes tipo;
                public readonly string data;
                public OnErrorReceivedEventArgs(Paquete.Paquetes tipo, string data)
                {
                    this.tipo = tipo;
                    this.data = data;
                }
            }
            public class OnConnectionStartEventArgs : EventArgs
            {
                public readonly string IP;
                public readonly int port;
                public OnConnectionStartEventArgs(string IP, int port)
                {
                    this.IP = IP;
                    this.port = port;
                }
            }
            public class OnDataArrivesEventArgs : EventArgs
            {
                public readonly string data;
                public OnDataArrivesEventArgs(string data)
                {
                    this.data = data;
                }
            }
            private List<string> StringToStringList(string s)
            {
                List<string> l = new List<string>();
                l.Add(s);
                return l;
            }
        }
        public class FTServer
        {
            private Thread lectordepaquetes;
            private UdpClient receivingUdpClient;
            private IPEndPoint RemoteIpEndPoint;
            private int TasaDeActualizacionDePaquetes = 100;
            private Socket sock;
            private int port = 3000;
            private int ClientPort;
            private bool running = true;
            List<Client> Clients = new List<Client>();

            public event EventHandler<OnClientConnectEventArgs> OnClientConnect;
            public event EventHandler<OnClientDisconnectEventArgs> OnClientDisconnects;
            public event EventHandler<OnUnknownPackageReceivedEventArgs> OnUnknownPackageReceived;

            public event EventHandler<OnFileDownloadStartedEventArgs> OnFileDownloadStarted;
            public event EventHandler<DirectoryInfoEventArgs> OnDirectoryInfoRequested;
            public event EventHandler<OnErrorSentEventArgs> OnErrorSent;

            public event EventHandler<EventArgs> OnServerStart;
            public event EventHandler<EventArgs> OnServerClose;

            public FTServer(int port)
            {
                this.port = port;
                this.ClientPort = port + 1;
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                lectordepaquetes = new Thread(LectorDePaquetes);
                lectordepaquetes.Start();
                OnServerStart?.Invoke(this, null);
            }

            public FTServer()
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                lectordepaquetes = new Thread(LectorDePaquetes);
                lectordepaquetes.Start();
                OnServerStart?.Invoke(this, null);
            }
            
            private void LectorDePaquetes()
            {
                receivingUdpClient = new UdpClient(port);
                RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
                while (running)
                {
                    try
                    {
                        Byte[] receiveBytes = receivingUdpClient.Receive(ref RemoteIpEndPoint);
                        string returnData = Encoding.ASCII.GetString(receiveBytes);
                        Paquete paquete = new Paquete(returnData);
                        switch (paquete.Tipo)
                        {
                            case Paquete.Paquetes.INICIO_COMUNICACION:
                                {
                                    Clients.Add(new Client(RemoteIpEndPoint, ClientPort));
                                    Paquete p = new Paquete(Paquete.Paquetes.CONFIRMAR_INICIO_COMUNICACION);
                                    p.Enviar(Clients.Last().IP, ClientPort);
                                    OnClientConnect?.Invoke(this, new OnClientConnectEventArgs(RemoteIpEndPoint));
                                    break;
                                }
                            case Paquete.Paquetes.CONFIRMAR_INICIO_COMUNICACION:
                                {
                                    break;
                                }
                            case Paquete.Paquetes.FIN_COMUNICACION:
                                {
                                    string ip;
                                    ip = RemoveClient(RemoteIpEndPoint);
                                    if (ip  != null)
                                    {
                                        OnClientDisconnects?.Invoke(this, new OnClientDisconnectEventArgs(ip));
                                    }
                                    break;
                                }
                            case Paquete.Paquetes.PAQUETE_DESCONOCIDO:
                                {
                                    OnUnknownPackageReceived?.Invoke(this, new OnUnknownPackageReceivedEventArgs(returnData));
                                    break;
                                }
                            case Paquete.Paquetes.SOLICITAR_ARCHIVOS_DIRECTORIO:
                                {
                                    if(Directory.Exists(paquete.parametros.ElementAt(0)))
                                    {
                                        OnDirectoryInfoRequested?.Invoke(this, new DirectoryInfoEventArgs(paquete.parametros.ElementAt(0)));
                                        List<string> files =
                                        Directory.GetFiles(paquete.parametros.ElementAt(0))                                       
                                        .AsEnumerable()
                                        .ToList();
                                        List<string> directory =
                                        Directory.GetDirectories(paquete.parametros.ElementAt(0))
                                        .Select(fileName => Path.GetFileNameWithoutExtension(fileName))
                                        .AsEnumerable()
                                        .ToList();

                                        foreach (string s in directory)
                                        {
                                            files.Add(s);
                                        }

                                        Paquete p = new Paquete(Paquete.Paquetes.RESPUESTA_SOLICITUD_ARCHIVOS_DIRECTORIO, files);
                                        p.Enviar(RemoteIpEndPoint.Address.MapToIPv4().ToString(), ClientPort);

                                    } else
                                    {
                                        OnErrorSent?.Invoke(this, new OnErrorSentEventArgs(Paquete.Paquetes.CARPETA_INEXISTENTE.ToString(), paquete.parametros.ElementAt(0)));
                                        Paquete p = new Paquete(Paquete.Paquetes.CARPETA_INEXISTENTE, paquete.parametros);
                                        p.Enviar(RemoteIpEndPoint.Address.MapToIPv4().ToString(), ClientPort);
                                    }
                                    break;
                                }
                            case Paquete.Paquetes.SOLICITAR_ARCHIVO:
                                {
                                    if(File.Exists(paquete.parametros.ElementAt(0)))
                                    {
                                        OnFileDownloadStarted?.Invoke(this, new OnFileDownloadStartedEventArgs(paquete.parametros.ElementAt(0)));
                                        Paquete p = new Paquete(Paquete.Paquetes.CONFIRMAR_SOLICITUD_ARCHIVO, paquete.parametros);
                                        p.Enviar(RemoteIpEndPoint.Address.MapToIPv4().ToString(), ClientPort);
                                        List<string> Content = File.ReadAllLines(paquete.parametros.ElementAt(0)).ToList();
                                        foreach(string s in Content)
                                        {
                                            Paquete contenido = new Paquete(Paquete.Paquetes.CONTENIDO_ARCHIVO, StringToStringList(s));
                                            contenido.Enviar(RemoteIpEndPoint.Address.MapToIPv4().ToString(), ClientPort);
                                        }
                                        Paquete fin_archivo = new Paquete(Paquete.Paquetes.FIN_DE_ARCHIVO, paquete.parametros);
                                        fin_archivo.Enviar(RemoteIpEndPoint.Address.MapToIPv4().ToString(),ClientPort);
                                    } else
                                    {
                                        OnErrorSent?.Invoke(this, new OnErrorSentEventArgs(Paquete.Paquetes.ARCHIVO_INEXISTENTE.ToString(), paquete.parametros.ElementAt(0)));
                                        Paquete p = new Paquete(Paquete.Paquetes.ARCHIVO_INEXISTENTE, paquete.parametros);
                                        p.Enviar(RemoteIpEndPoint.Address.MapToIPv4().ToString(), ClientPort);
                                    }
                                    break;
                                }
                            case Paquete.Paquetes.SOLICITAR_CARPETA_ESPECIAL:
                                {

                                    string path;
                                    if(paquete.parametros.ElementAt(0).CompareTo(Paquete.CARPETAS_ESPECIALES.APPLICATION_DATA.ToString()) == 0)
                                    {
                                        path = SpecialDirectories.AllUsersApplicationData;
                                    }
                                    else if (paquete.parametros.ElementAt(0).CompareTo(Paquete.CARPETAS_ESPECIALES.CURRENT_USER_APPLICATION_DATA.ToString()) == 0)
                                    {
                                        path = SpecialDirectories.CurrentUserApplicationData;
                                    }
                                    else if (paquete.parametros.ElementAt(0).CompareTo(Paquete.CARPETAS_ESPECIALES.DESKTOP.ToString()) == 0)
                                    {
                                        path = SpecialDirectories.Desktop;
                                    }
                                    else if (paquete.parametros.ElementAt(0).CompareTo(Paquete.CARPETAS_ESPECIALES.MyDocuments.ToString()) == 0)
                                    {
                                        path = SpecialDirectories.MyDocuments;
                                    } 
                                    else if (paquete.parametros.ElementAt(0).CompareTo(Paquete.CARPETAS_ESPECIALES.MyMusic.ToString()) == 0)
                                    {
                                        path = SpecialDirectories.MyMusic;
                                    }
                                    else if (paquete.parametros.ElementAt(0).CompareTo(Paquete.CARPETAS_ESPECIALES.MyPictures.ToString()) == 0)
                                    {
                                        path = SpecialDirectories.MyPictures;
                                    }
                                    else if (paquete.parametros.ElementAt(0).CompareTo(Paquete.CARPETAS_ESPECIALES.ProgramFiles.ToString()) == 0)
                                    {
                                        path = SpecialDirectories.ProgramFiles;
                                    }
                                    else if (paquete.parametros.ElementAt(0).CompareTo(Paquete.CARPETAS_ESPECIALES.Programs.ToString()) == 0)
                                    {
                                        path = SpecialDirectories.Programs;
                                    }
                                    else if (paquete.parametros.ElementAt(0).CompareTo(Paquete.CARPETAS_ESPECIALES.Temp.ToString()) == 0)
                                    {
                                        path = SpecialDirectories.Temp;
                                    }
                                    else
                                    {
                                        path = Paquete.Paquetes.CARPETA_INEXISTENTE.ToString();
                                    }
                                    Paquete p = new Paquete(Paquete.Paquetes.RESPUESTA_SOLICITUD_CARPETA_ESPECIAL, StringToStringList(path));
                                    p.Enviar(RemoteIpEndPoint.Address.MapToIPv4().ToString(), ClientPort);
                                    break;
                                }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message.ToString());
                    }
                    Thread.Sleep(TasaDeActualizacionDePaquetes);
                }
                OnServerClose?.Invoke(this, null);
            }
            
            private List<string> StringToStringList(string s)
            {
                List<string> l = new List<string>();
                l.Add(s);
                return l;
            }

            public void Shutdown()
            {
                if(running)
                {
                    running = false;
                }
                foreach(Client client in Clients)
                {
                    client.Disconnect();
                }
                sock.Close();
            }
            
            internal class Client
            {
                public IPEndPoint Data;
                public string IP;
                int port;
                public Client(IPEndPoint addr, int port)
                {
                    this.IP = addr.Address.MapToIPv4().ToString();
                    this.Data = addr;
                    this.port = port;
                }
                public void Disconnect()
                {
                    Paquete p = new Paquete(Paquete.Paquetes.FIN_COMUNICACION);
                    p.Enviar(this.IP, port);
                }
            }

            private string RemoveClient(IPEndPoint iPEnd)
            {
                string IP = null;
                foreach(Client client in Clients)
                {
                    if(client.IP.CompareTo(iPEnd.Address.MapToIPv4().ToString()) == 0)
                    {
                        IP = client.IP;
                        Clients.Remove(client);
                    }
                }
                return IP;
            }

            public class OnClientConnectEventArgs : EventArgs
            {
                private readonly IPEndPoint endPoint;
                public OnClientConnectEventArgs(IPEndPoint _endPoint)
                {
                    this.endPoint = _endPoint;
                }

                public IPEndPoint EndPoint
                {
                    get { return this.endPoint; }
                }
            }
            public class OnClientDisconnectEventArgs : EventArgs
            {
                private readonly string _IP;
                public OnClientDisconnectEventArgs(string IP)
                {
                    this._IP = IP;
                }

                public string IP
                {
                    get { return this._IP; }
                }
            }
            public class OnFileDownloadStartedEventArgs : EventArgs
            {
                private readonly string file;
                public OnFileDownloadStartedEventArgs(string file)
                {
                    this.file = file;
                }

                public string File
                {
                    get { return this.file; }
                }
            }
            public class DirectoryInfoEventArgs : EventArgs
            {
                private readonly string directory;
                public DirectoryInfoEventArgs(string directory)
                {
                    this.directory = directory;
                }

                public string Directory
                {
                    get { return this.directory; }
                }
            }
            public class OnUnknownPackageReceivedEventArgs : EventArgs
            {
                public readonly string Package;
                public OnUnknownPackageReceivedEventArgs(string package)
                {
                    this.Package = package;
                }
            }
            public class OnErrorSentEventArgs : EventArgs
            {
                public readonly string description;
                public readonly string cause;
                public OnErrorSentEventArgs(string description, string cause)
                {
                    this.cause = cause;
                    this.description = description;
                }
            }
        }

        internal class Paquete
        {
            protected Socket sock;
            protected string Informacion;
            internal protected const string DataSeparator = "@";

            public List<string> parametros = new List<string>();
            public Paquetes Tipo;



            internal enum CARPETAS_ESPECIALES
            {
                APPLICATION_DATA,
                CURRENT_USER_APPLICATION_DATA,
                DESKTOP,
                MyDocuments,
                MyMusic,
                MyPictures,
                ProgramFiles,
                Programs,
                Temp
            }

            internal enum Paquetes
            {
                INICIO_COMUNICACION,
                CONFIRMAR_INICIO_COMUNICACION,
                FIN_COMUNICACION,
                PAQUETE_DESCONOCIDO,
                SOLICITAR_ARCHIVO,
                CONFIRMAR_SOLICITUD_ARCHIVO,
                CONTENIDO_ARCHIVO,
                FIN_DE_ARCHIVO,
                SOLICITAR_ARCHIVOS_DIRECTORIO,
                RESPUESTA_SOLICITUD_ARCHIVOS_DIRECTORIO,
                CARPETA_INEXISTENTE,
                ARCHIVO_INEXISTENTE,
                SOLICITAR_CARPETA_ESPECIAL,
                RESPUESTA_SOLICITUD_CARPETA_ESPECIAL
            }

            internal Paquete(Paquetes paquete)
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                CrearPaquete(paquete);
            }

            internal Paquete(Paquetes paquete, List<string> data)
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                CrearPaquete(paquete, data);
            }

            internal Paquete(string data)
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                var Datos = data.Split(DataSeparator);
                if (Datos.Count() > 0)
                {
                    this.Tipo = ObtenerTipoDePaquete(Datos.ElementAt(1));
                    if (Datos.Count() > 1)
                    {
                        for (int i = 2; i < Datos.Count(); i++)
                        {
                            parametros.Add(Datos.ElementAt(i));
                        }
                    }
                }
            }


            internal void Enviar(string IP, int puerto)
            {
                IPAddress serverAddr = IPAddress.Parse(IP);
                IPEndPoint endPoint = new IPEndPoint(serverAddr, puerto);
                sock.SendTo(Encoding.ASCII.GetBytes(Informacion), endPoint);
                Console.WriteLine("Paquete enviado ->" + Informacion);
            }
            protected void CrearPaquete(Paquetes paquetes, List<string> data = null)
            {
                Informacion = DataSeparator + paquetes.ToString();
                if (data != null)
                {
                    foreach (string d in data)
                    {
                        Informacion += DataSeparator + d;
                    }
                }
            }
            private Paquete.Paquetes ObtenerTipoDePaquete(string s)
            {
                if (s.CompareTo(Paquete.Paquetes.INICIO_COMUNICACION.ToString()) == 0)
                {
                    return Paquete.Paquetes.INICIO_COMUNICACION;
                }
                else if (s.CompareTo(Paquete.Paquetes.CONFIRMAR_INICIO_COMUNICACION.ToString()) == 0)
                {
                    return Paquete.Paquetes.CONFIRMAR_INICIO_COMUNICACION;
                }
                else if (s.CompareTo(Paquete.Paquetes.FIN_COMUNICACION.ToString()) == 0)
                {
                    return Paquete.Paquetes.FIN_COMUNICACION;
                }
                else if (s.CompareTo(Paquete.Paquetes.SOLICITAR_ARCHIVO.ToString()) == 0)
                {
                    return Paquete.Paquetes.SOLICITAR_ARCHIVO;
                }
                else if (s.CompareTo(Paquete.Paquetes.CONFIRMAR_SOLICITUD_ARCHIVO.ToString()) == 0)
                {
                    return Paquete.Paquetes.CONFIRMAR_SOLICITUD_ARCHIVO;
                }
                else if (s.CompareTo(Paquete.Paquetes.CONTENIDO_ARCHIVO.ToString()) == 0)
                {
                    return Paquete.Paquetes.CONTENIDO_ARCHIVO;
                }
                else if (s.CompareTo(Paquete.Paquetes.FIN_DE_ARCHIVO.ToString()) == 0)
                {
                    return Paquete.Paquetes.FIN_DE_ARCHIVO;
                }
                else if (s.CompareTo(Paquete.Paquetes.SOLICITAR_ARCHIVOS_DIRECTORIO.ToString()) == 0)
                {
                    return Paquete.Paquetes.SOLICITAR_ARCHIVOS_DIRECTORIO;
                }
                else if (s.CompareTo(Paquete.Paquetes.RESPUESTA_SOLICITUD_ARCHIVOS_DIRECTORIO.ToString()) == 0)
                {
                    return Paquete.Paquetes.RESPUESTA_SOLICITUD_ARCHIVOS_DIRECTORIO;
                }
                else if (s.CompareTo(Paquete.Paquetes.ARCHIVO_INEXISTENTE.ToString()) == 0)
                {
                    return Paquete.Paquetes.ARCHIVO_INEXISTENTE;
                }
                else if (s.CompareTo(Paquete.Paquetes.CARPETA_INEXISTENTE.ToString()) == 0)
                {
                    return Paquete.Paquetes.CARPETA_INEXISTENTE;
                }
                else if (s.CompareTo(Paquete.Paquetes.SOLICITAR_CARPETA_ESPECIAL.ToString()) == 0)
                {
                    return Paquete.Paquetes.SOLICITAR_CARPETA_ESPECIAL;
                }
                else if (s.CompareTo(Paquete.Paquetes.RESPUESTA_SOLICITUD_CARPETA_ESPECIAL.ToString()) == 0)
                {
                    return Paquete.Paquetes.RESPUESTA_SOLICITUD_CARPETA_ESPECIAL;
                }
                else return Paquete.Paquetes.PAQUETE_DESCONOCIDO;
            }
        }
        public static void PrintList(List<string> s)
        {
            int index = 0;
            foreach (string n in s)
            {
                Console.WriteLine("Indice: " + index + " Valor:" + n);
                index++;
            }
        }
    }
}
