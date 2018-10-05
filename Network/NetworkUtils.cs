﻿using DLT.Meta;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DLT
{
    namespace Network
    {
        public class NetworkUtils
        {

            public static string resolveHostname(string hostname)
            {
                // Check for IP
                IPAddress address;
                if (IPAddress.TryParse(hostname, out address))
                {
                    switch (address.AddressFamily)
                    {
                        case System.Net.Sockets.AddressFamily.InterNetwork:
                            return hostname;
                        case System.Net.Sockets.AddressFamily.InterNetworkV6:
                            return hostname;
                        default:
                            break;
                    }
                }

                // Check DNS
                try
                {
                    IPHostEntry hostEntry;
                    hostEntry = Dns.GetHostEntry(hostname);

                    // TODO: handle multi-ip hostnames
                    foreach (var ip in hostEntry.AddressList)
                    {
                        // TODO: handle IPv6 as well
                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                        {
                            return ip.ToString();
                        }
                    }
                }
                catch (Exception)
                {
                    return hostname;
                }

                return "";
            }


        }

    }

}
