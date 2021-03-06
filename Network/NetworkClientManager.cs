﻿using IXICore.Meta;
using IXICore.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace IXICore.Network
{
    public class NetworkClientManager
    {
        public static List<NetworkClient> networkClients = new List<NetworkClient>();
        private static List<string> connectingClients = new List<string>(); // A list of clients that we're currently connecting

        private static Thread reconnectThread;
        private static bool autoReconnect = true;

        private static bool running = false;
        private static ThreadLiveCheck TLC;

        // Starts the Network Client Manager. First it connects to one of the seed nodes in order to fetch the Presence List.
        // Afterwards, it starts the reconnect and keepalive threads
        public static void start()
        {
            if(running)
            {
                return;
            }

            if(CoreConfig.preventNetworkOperations)
            {
                Logging.warn("Not starting NetworkClientManager thread due to preventNetworkOperations flag being set.");
                return;
            }

            running = true;
            networkClients = new List<NetworkClient>();

            PeerStorage.readPeersFile();

            // Now add the seed nodes to the list
            foreach (string[] addr in CoreNetworkUtils.getSeedNodes(CoreConfig.isTestNet))
            {
                byte[] wallet_addr = null;
                if(addr[1] != null)
                {
                    wallet_addr = Base58Check.Base58CheckEncoding.DecodePlain(addr[1]);
                }
                PeerStorage.addPeerToPeerList(addr[0], wallet_addr, false);
            }

            // Connect to a random node first
            bool firstSeedConnected = false;
            while (firstSeedConnected == false && IxianHandler.forceShutdown == false)
            {
                Peer p = PeerStorage.getRandomMasterNodeAddress();
                if (p != null)
                {
                    firstSeedConnected = connectTo(p.hostname, p.walletAddress);
                }
                if (firstSeedConnected == false)
                {
                    Thread.Sleep(1000);
                }
            }

            // Start the reconnect thread
            TLC = new ThreadLiveCheck();
            reconnectThread = new Thread(reconnectClients);
            reconnectThread.Name = "Network_Client_Manager_Reconnect";
            autoReconnect = true;
            reconnectThread.Start();
        }

        // Starts the Network Client Manager in debug mode with a single connection and no reconnect. Used for development only.
        public static bool startWithSingleConnection(string address)
        {
            if (running)
            {
                return false;
            }
            running = true;
            networkClients = new List<NetworkClient>();

            bool result = connectTo(address, null);
            if(!result)
            {
                running = false;
            }

            return result;
        }

        public static void stop()
        {
            if(!running)
            {
                return;
            }
            running = false;
            autoReconnect = false;
            isolate();

            // Force stopping of reconnect thread
            if (reconnectThread == null)
                return;
            reconnectThread.Abort();
            reconnectThread = null;
        }

        // Immediately disconnects all clients
        public static void isolate()
        {
            Logging.info("Isolating network clients...");

            lock (networkClients)
            {
                // Disconnect each client
                foreach (NetworkClient client in networkClients)
                {
                    client.stop();
                }

                // Empty the client list
                networkClients.Clear();
            }
        }

        // Reconnects to network clients
        public static void restartClients()
        {
            Logging.info("Stopping network clients...");
            stop();
            Thread.Sleep(100);
            Logging.info("Starting network clients...");
            start();
        }

        // Connects to a specified node, with the syntax host:port
        public static bool connectTo(string host, byte[] wallet_address)
        {
            if (host == null || host.Length < 3)
            {
                Logging.error(String.Format("Invalid host address {0}", host));
                return false;
            }

            string[] server = host.Split(':');
            if (server.Count() < 2)
            {
                Logging.warn(string.Format("Cannot connect to invalid hostname: {0}", host));
                return false;
            }

            // Resolve the hostname first
            string resolved_server_name = NetworkUtils.resolveHostname(server[0]);

            // Skip hostnames we can't resolve
            if (resolved_server_name.Length < 1)
            {
                Logging.warn(string.Format("Cannot resolve IP for {0}, skipping connection.", server[0]));
                return false;
            }

            string resolved_host = string.Format("{0}:{1}", resolved_server_name, server[1]);

            if (NetworkServer.isRunning())
            {
                // Verify against the publicly disclosed ip
                // Don't connect to self
                if (resolved_server_name.Equals(IxianHandler.publicIP, StringComparison.Ordinal))
                {
                    if (server[1].Equals(string.Format("{0}", IxianHandler.publicPort), StringComparison.Ordinal))
                    {
                        Logging.info(string.Format("Skipping connection to public self seed node {0}", host));
                        return false;
                    }
                }

                // Get all self addresses and run through them
                List<string> self_addresses = CoreNetworkUtils.GetAllLocalIPAddresses();
                foreach (string self_address in self_addresses)
                {
                    // Don't connect to self
                    if (resolved_server_name.Equals(self_address, StringComparison.Ordinal))
                    {
                        if (server[1].Equals(string.Format("{0}", IxianHandler.publicPort), StringComparison.Ordinal))
                        {
                            Logging.info(string.Format("Skipping connection to self seed node {0}", host));
                            return false;
                        }
                    }
                }
            }

            lock (connectingClients)
            {
                foreach (string client in connectingClients)
                {
                    if (resolved_host.Equals(client, StringComparison.Ordinal))
                    {
                        // We're already connecting to this client
                        return false;
                    }
                }

                // The the client to the connecting clients list
                connectingClients.Add(resolved_host);
            }

            // Check if node is already in the client list
            lock (networkClients)
            {
                foreach (NetworkClient client in networkClients)
                {
                    if (client.getFullAddress(true).Equals(resolved_host, StringComparison.Ordinal))
                    {
                        // Address is already in the client list
                        return false;
                    }
                }
            }

            // Check if node is already in the server list
            string[] connectedClients = NetworkServer.getConnectedClients(true);
            for (int i = 0; i < connectedClients.Length; i++)
            {
                if (connectedClients[i].Equals(resolved_host, StringComparison.Ordinal))
                {
                    // Address is already in the client list
                    return false;
                }
            }

            // Connect to the specified node
            NetworkClient new_client = new NetworkClient();
            // Recompose the connection address from the resolved IP and the original port
            bool result = new_client.connectToServer(resolved_server_name, Convert.ToInt32(server[1]), wallet_address);

            // Add this node to the client list if connection was successfull
            if (result == true)
            {
                // Add this node to the client list
                lock (networkClients)
                {
                    networkClients.Add(new_client);
                }

            }

            // Remove this node from the connecting clients list
            lock (connectingClients)
            {
                connectingClients.Remove(resolved_host);
            }

            return result;
        }

        // Send data to all connected nodes
        // Returns true if the data was sent to at least one client
        public static bool broadcastData(char[] types, ProtocolMessageCode code, byte[] data, byte[] helper_data, RemoteEndpoint skipEndpoint = null)
        {
            bool result = false;
            lock (networkClients)
            {
                foreach (NetworkClient client in networkClients)
                {
                    if (skipEndpoint != null)
                    {
                        if (client == skipEndpoint)
                            continue;
                    }

                    if (!client.isConnected())
                    {
                        continue;
                    }

                    if (client.helloReceived == false)
                    {
                        continue;
                    }

                    if (types != null)
                    {
                        if (client.presenceAddress == null || !types.Contains(client.presenceAddress.type))
                        {
                            continue;
                        }
                    }


                    client.sendData(code, data, helper_data);
                    result = true;
                }
            }
            return result;
        }

        public static bool sendToClient(string neighbor, ProtocolMessageCode code, byte[] data, byte[] helper_data)
        {
            NetworkClient client = null;
            lock (networkClients)
            {
                foreach (NetworkClient c in networkClients)
                {
                    if (c.getFullAddress() == neighbor)
                    {
                        client = c;
                        break;
                    }
                }
            }

            if (client != null)
            {
                client.sendData(code, data, helper_data);
                return true;
            }

            return false;
        }

        // Returns all the connected clients
        public static string[] getConnectedClients(bool only_fully_connected = false)
        {
            List<String> result = new List<String>();

            lock (networkClients)
            {
                foreach (NetworkClient client in networkClients)
                {
                    if (client.isConnected())
                    {
                        if (only_fully_connected && !client.helloReceived)
                        {
                            continue;
                        }

                        try
                        {
                            string client_name = client.getFullAddress();
                            result.Add(client_name);
                        }
                        catch (Exception e)
                        {
                            Logging.error(string.Format("NetworkClientManager->getConnectedClients: {0}", e.ToString()));
                        }
                    }
                }
            }

            return result.ToArray();
        }


        /// <summary>
        ///  Recalculates local time difference depending on 2/3rd of connected servers time differences.
        ///  Maximum time difference is enforced with CoreConfig.maxTimeDifferenceAdjustment.
        ///  If CoreConfig.forceTimeOffset is used, both Clock.networkTimeDifference and
        ///  Clock.realNetworkTimeDifference will be forced to the value of CoreConfig.forceTimeOffset.
        /// </summary>
        public static void recalculateLocalTimeDifference()
        {
            if(CoreConfig.forceTimeOffset != int.MaxValue)
            {
                Clock.networkTimeDifference = CoreConfig.forceTimeOffset;
                Clock.realNetworkTimeDifference = CoreConfig.forceTimeOffset;
                return;
            }
            lock (networkClients)
            {
                if (networkClients.Count < 3)
                    return;

                long total_time_diff = 0;

                List<long> time_diffs = new List<long>();

                foreach (NetworkClient client in networkClients)
                {
                    if (client.helloReceived && client.timeSyncComplete)
                    {
                        time_diffs.Add(client.timeDifference);
                    }
                }

                time_diffs.Sort();

                var time_diffs_majority = time_diffs.Take((time_diffs.Count * 2 / 3) + 1);

                foreach (long time in time_diffs_majority)
                {
                    total_time_diff += time;
                }

                long timeDiff = total_time_diff / time_diffs_majority.Count();

                Clock.realNetworkTimeDifference = timeDiff;

                if(PresenceList.myPresenceType == 'M' && PresenceList.myPresenceType == 'H')
                {
                    // if Master/full History node, do time adjustment within max time difference
                    if (timeDiff > CoreConfig.maxTimeDifferenceAdjustment)
                    {
                        Clock.networkTimeDifference = CoreConfig.maxTimeDifferenceAdjustment;
                    }
                    else if (timeDiff < -CoreConfig.maxTimeDifferenceAdjustment)
                    {
                        Clock.networkTimeDifference = -CoreConfig.maxTimeDifferenceAdjustment;
                    }
                    else
                    {
                        Clock.networkTimeDifference = timeDiff;
                    }
                }else
                {
                    // If non-Master/full History node adjust time to network time
                    Clock.networkTimeDifference = timeDiff;
                }
            }
        }


        // Returns a random new potential neighbor. Returns null if no new neighbor is found.
        public static Peer scanForNeighbor()
        {
            Peer connectToPeer = null;
            // Find only masternodes
            while (connectToPeer == null)
            {
                bool addr_valid = true;
                Peer p = PeerStorage.getRandomMasterNodeAddress();

                if (p == null)
                {
                    break;
                }

                // Next, check if we're connecting to a self address of this node
                string[] server = p.hostname.Split(':');

                if (server.Length < 2)
                {
                    break;
                }

                // Resolve the hostname first
                string resolved_server_name = NetworkUtils.resolveHostname(server[0]);
                string resolved_server_name_with_port = resolved_server_name + ":" + server[1];

                // Check if we are already connected to this address
                lock (networkClients)
                {
                    foreach (NetworkClient client in networkClients)
                    {
                        if (client.getFullAddress(true).Equals(resolved_server_name_with_port, StringComparison.Ordinal))
                        {
                            // Address is already in the client list
                            addr_valid = false;
                            break;
                        }
                    }
                }

                // Check if node is already in the server list
                string[] connectedClients = NetworkServer.getConnectedClients(true);
                for (int i = 0; i < connectedClients.Length; i++)
                {
                    if (connectedClients[i].Equals(resolved_server_name_with_port, StringComparison.Ordinal))
                    {
                        // Address is already in the client list
                        addr_valid = false;
                        break;
                    }
                }

                if (addr_valid == false)
                    continue;

                // Check against connecting clients list as well
                lock (connectingClients)
                {
                    foreach (string client in connectingClients)
                    {
                        if (resolved_server_name_with_port.Equals(client, StringComparison.Ordinal))
                        {
                            // Address is already in the connecting client list
                            addr_valid = false;
                            break;
                        }
                    }

                }

                if (addr_valid == false)
                    continue;

                if (NetworkServer.isRunning())
                {
                    // Get all self addresses and run through them
                    List<string> self_addresses = CoreNetworkUtils.GetAllLocalIPAddresses();
                    foreach (string self_address in self_addresses)
                    {
                        // Don't connect to self
                        if (resolved_server_name.Equals(self_address, StringComparison.Ordinal))
                        {
                            if (server[1].Equals(string.Format("{0}", IxianHandler.publicPort), StringComparison.Ordinal))
                            {
                                addr_valid = false;
                            }
                        }
                    }
                }

                // If the address is valid, add it to the candidates
                if (addr_valid)
                {
                    connectToPeer = p;
                }
            }

            return connectToPeer;
        }

        // Scan for and connect to a new neighbor
        private static void connectToRandomNeighbor()
        {
            Peer neighbor = scanForNeighbor();
            if (neighbor != null)
            {
                Logging.info(string.Format("Attempting to add new neighbor: {0}", neighbor.hostname));
                connectTo(neighbor.hostname, neighbor.walletAddress);
            }
        }

        // Checks for missing clients
        private static void reconnectClients()
        {
            Random rnd = new Random();

            // Wait 5 seconds before starting the loop
            Thread.Sleep(CoreConfig.networkClientReconnectInterval);

            while (autoReconnect)
            {
                TLC.Report();
                handleDisconnectedClients();

                // Check if we need to connect to more neighbors
                if (getConnectedClients().Count() < CoreConfig.simultaneousConnectedNeighbors)
                {
                    // Scan for and connect to a new neighbor
                    connectToRandomNeighbor();
                }
                else if (getConnectedClients(true).Count() > CoreConfig.simultaneousConnectedNeighbors)
                {
                    List<NetworkClient> netClients = null;
                    lock (networkClients)
                    {
                        netClients = new List<NetworkClient>(networkClients);
                    }
                    CoreProtocolMessage.sendBye(netClients[0], ProtocolByeCode.bye, "Disconnected for shuffling purposes.", "", false);

                    lock (networkClients)
                    {
                        networkClients.Remove(netClients[0]);
                    }
                }

                // Connect randomly to a new node. Currently a 1% chance to reconnect during this iteration
                if (rnd.Next(100) == 1)
                {
                    connectToRandomNeighbor();
                }

                // Wait 5 seconds before rechecking
                Thread.Sleep(CoreConfig.networkClientReconnectInterval);
            }
        }

        private static void handleDisconnectedClients()
        {
            List<NetworkClient> netClients = null;
            lock (networkClients)
            {
                netClients = new List<NetworkClient>(networkClients);
            }

            // Prepare a list of failed clients
            List<NetworkClient> failed_clients = new List<NetworkClient>();

            List<NetworkClient> dup_clients = new List<NetworkClient>();

            foreach (NetworkClient client in netClients)
            {
                if (dup_clients.Find(x => x.getFullAddress(true) == client.getFullAddress(true)) != null)
                {
                    failed_clients.Add(client);
                    continue;
                }
                dup_clients.Add(client);
                if (client.isConnected())
                {
                    continue;
                }
                // Check if we exceeded the maximum reconnect count
                if (client.getTotalReconnectsCount() >= CoreConfig.maximumNeighborReconnectCount || client.fullyStopped)
                {
                    // Remove this client so we can search for a new neighbor
                    failed_clients.Add(client);
                }
                else
                {
                    // Reconnect
                    client.reconnect();
                }
            }

            // Go through the list of failed clients and remove them
            foreach (NetworkClient client in failed_clients)
            {
                client.stop();
                lock (networkClients)
                {
                    networkClients.Remove(client);
                }
                // Remove this node from the connecting clients list
                lock (connectingClients)
                {
                    connectingClients.Remove(client.getFullAddress(true));
                }
            }
        }


        public static int getQueuedMessageCount()
        {
            int messageCount = 0;
            lock (networkClients)
            {
                foreach (NetworkClient client in networkClients)
                {
                    messageCount += client.getQueuedMessageCount();
                }
            }
            return messageCount;
        }

        public static NetworkClient getClient(int idx)
        {
            lock (networkClients)
            {
                int i = 0;
                NetworkClient lastClient = null;
                foreach(NetworkClient client in networkClients)
                {
                    if(client.isConnected())
                    {
                        lastClient = client;
                    }
                    if(i == idx && lastClient != null)
                    {
                        break;
                    }
                    i++;
                }
                return lastClient;
            }
        }
    }
}
