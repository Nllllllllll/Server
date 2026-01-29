using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace PersistenceServer
{
    public class UserConnection
    {
        private readonly MmoWsServer _mmoWsServer;
        private readonly WebSocket _webSocket;
        public Guid Id { get; private set; }
        public string Cookie { get; set; }
        public IPAddress Ip { get; set; }
        private string? _macAddress;  // NOUVEAU

        // La taille maximale autorisée pour un message (par ex., 1 MB)
        const long MaxMessageSize = 1 * 1024 * 1024; // 1 MB

        public UserConnection(MmoWsServer server, WebSocket webSocket, IPAddress ip)
        {
            _mmoWsServer = server;
            _webSocket = webSocket;
            Id = Guid.NewGuid();
            Cookie = "";
            Ip = ip;
            _macAddress = null;  // NOUVEAU
        }

        // NOUVELLES MÉTHODES
        public string? GetIpAddress()
        {
            return Ip?.ToString();
        }

        public string? GetMacAddress()
        {
            return _macAddress;
        }

        public void SetMacAddress(string? macAddress)
        {
            _macAddress = macAddress;
        }

        public async Task HandleConnectionAsync()
        {
            // Notifier le serveur qu'une connexion a été établie.
            InvokeOnConnected();

            // Buffer de 2 Ko, pour accueillir environ 500 caractères de longueur
            // si le message dépasse le buffer, il sera accumulé jusqu'à ce qu'il soit entièrement reçu
            // s'il dépasse la taille maximale pendant l'accumulation, l'utilisateur sera déconnecté
            var buffer = new ArraySegment<byte>(new byte[2048]);
            MemoryStream messageStream = new();

            try
            {
                while (_webSocket.State == WebSocketState.Open)
                {
                    // quand ReceiveAsync n'a rien à recevoir, ce thread est retourné au pool de threads et est libre de gérer d'autres requêtes entrantes ou d'effectuer d'autres tâches
                    var result = await _webSocket.ReceiveAsync(buffer, CancellationToken.None);
                    if (buffer.Array != null)
                    {
                        messageStream.Write(buffer.Array, buffer.Offset, result.Count);

                        // Vérifier que la taille du message accumulé ne dépasse pas la taille maximale autorisée
                        if (messageStream.Length > MaxMessageSize)
                        {
                            Console.WriteLine("Le client a envoyé un message dépassant la taille maximale autorisée. Déconnexion du client.");
                            InvokeOnDisconnected();
                            await _webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Taille du message dépassée", CancellationToken.None);
                            messageStream.SetLength(0); // Vider le flux
                            return;
                        }
                    }
                    // Si on a reçu la fin du message, on le traite, sinon on continue de l'accumuler
                    if (result.EndOfMessage)
                    { 
                        switch (result.MessageType)
                        {
                            /* MESSAGE TEXTE */
                            case WebSocketMessageType.Text:
                        Console.WriteLine("Les messages texte ne sont pas traités");
                        messageStream.SetLength(0);  // Vider le flux
                                break;

                        /* MESSAGE BINAIRE */
                        case WebSocketMessageType.Binary:
                        messageStream.Position = 0;  // Réinitialiser la position du flux
                        BinaryReader reader = new(messageStream, Encoding.ASCII);

                        while (reader.PeekChar() > 0) // -1 signifie "fin du flux", "0" signifie "indéfini"
                        {
                            RpcType rpcPrefix = (RpcType)reader.ReadByte();
                            _mmoWsServer.InvokeOnMessageReceived(rpcPrefix, this, reader);
                        }
                        messageStream.SetLength(0);  // Vider le flux pour le prochain message
                        break;

                        /* MESSAGE DE FERMETURE */
                        case WebSocketMessageType.Close:
                        InvokeOnDisconnected();
                        if (result.CloseStatus.HasValue)
                        {
                            await _webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription ?? string.Empty, CancellationToken.None);
                        }
                        else
                        {
                            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Fermeture de la connexion", CancellationToken.None);
                        }
                        messageStream.SetLength(0);  // Vider le flux
                        break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Gérer les exceptions potentielles pendant la communication WebSocket.
                Console.WriteLine($"La session WebSocket a détecté une erreur: {ex.Message}");
                InvokeOnDisconnected();
            }
        }

        public void Send(byte[] binaryMsg)
        {
            _webSocket.SendAsync(binaryMsg, WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        private void InvokeOnConnected()
        {
            BinaryReader reader = new(new MemoryStream(Array.Empty<byte>()));
            _mmoWsServer.InvokeOnMessageReceived(RpcType.RpcConnected, this, reader);
        }

        private void InvokeOnDisconnected()
        {
            BinaryReader reader = new(new MemoryStream(Array.Empty<byte>()));
            _mmoWsServer.InvokeOnMessageReceived(RpcType.RpcDisconnected, this, reader);
        }

        public async Task Disconnect()
        {
            InvokeOnDisconnected();
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Fermeture de la connexion", CancellationToken.None);
        }
    }
}
