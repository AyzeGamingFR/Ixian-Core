﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace IXICore
{
    /// <summary>
    ///  Allows a client to generate a secret value with incorporated markers for multiple addresses.
    ///  Such a value obscures which addresses it represents, but allows others to check whether a given address is part of the set
    ///  or not with a configurable degree of false-positives.
    ///  See also `AddressMatcher`.
    /// </summary>
    /// <remarks>
    ///  This mechanism is used by clients, who do not wish to disclose all their addresses ahead of time, to 'register' to an untrusted DLT Node
    ///  in order to receive information (events) when the client's addresses are encountered on the blockchain. It would be unfeasible for the DLT
    ///  Node to send all events to all clients (bandwidth prohibitative), so some filtering is required. By using this object, the client sends the
    ///  DLT Node a 'condensed' piece of information which matches multiple addresses, *including* the ones the client is interested in. There are false
    ///  positives, but never false negatives.
    ///  Of course, the DLT Node may record which addresses match the 'matcher', but it is impossible to determine which of the matches are false positives and
    ///  which are real.
    ///  Whether a given address truly belongs to a client can only be determined with 100% accuracy when the client chooses to use it - send a transaction from
    ///  that address, thus revealing the public key and nonce associated with it.
    /// </remarks>
    public class AddressClient
    {
        private List<byte[]> addresses;
        private int randomSeedValue;
        private Random rng = null;

        public AddressClient()
        {
            addresses = new List<byte[]>();
            randomSeedValue = 0;
            rng = new Random();
        }

        /// <summary>
        /// Creates an address matcher generator with the specified entropy bytes.
        ///  This will cause the matcher to always return the same matcher value if the same addresses are input,
        ///  thereby preventing some types of statistical analysis a malicious DLT or S2 node could perform by
        ///  asking the client to repeatedly generate new matchers.
        /// </summary>
        /// <remarks>
        ///  In order for the same matcher values to be generatd, the entropy bytes must also be the same.
        ///  This value must be preserved between client starts. A good candidate here is part of the private key (d value).
        /// </remarks>
        /// <param name="entropy">Bytes with some random state.</param>
        public AddressClient(byte[] entropy)
        {
            addresses = new List<byte[]>();
            byte[] hash = Crypto.sha512sqTrunc(entropy);
            uint offset_into = BitConverter.ToUInt32(hash, 0);
            offset_into = (uint)(offset_into % hash.Length);
            randomSeedValue = BitConverter.ToInt32(hash, (int)offset_into);
            rng = new Random(randomSeedValue);
        }

        /// <summary>
        ///  Adds an addres to the matcher.
        /// </summary>
        /// <param name="addr">Ixian wallet address.</param>
        public void addAddress(byte[] addr)
        {
            if (!addresses.Any(a => a.SequenceEqual(addr)))
            {
                addresses.Add(addr);
            }
        }

        /// <summary>
        /// Removes all addresses from the matcher.
        /// </summary>
        public void clearAddresses()
        {
            addresses.Clear();
        }

        /// <summary>
        /// Retrieves all addresses from the matcher.
        /// </summary>
        /// <returns>All addresses in the matcher.</returns>
        public List<byte[]> getAddresses()
        {
            return addresses;
        }

        /// <summary>
        /// Retrieves the number of addresses currently in the matcher.
        /// </summary>
        /// <returns></returns>
        public int numAddresses()
        {
            return addresses.Count;
        }

        private int getRandomUnfilledByte(byte[] filled)
        {
            int possible_rnd = rng.Next(filled.Length) + 1;
            int i = 0;
            while (true)
            {
                if (filled[i] == 0)
                {
                    possible_rnd--;
                    if (possible_rnd == 0) return i;
                }
                i++;
                if (i >= filled.Length) { i = 0; }
            }
        }

        /// <summary>
        ///  Generates the 'matcher' value (see description of this class `AddressClient`.
        /// </summary>
        /// <remarks>
        ///  The `bytes_per_addr` value determines how many false positives are likely to occur when matching random addresses. A smaller
        ///  value will produce more false positives, straining the bandwith and making the client process more data, while a higher number will
        ///  yield fewer false positives, but give away client's addresses with more certainty. 2-4 are usually good values.
        ///  Please note that both parties must agree on what value to use in `bytes_per_addr` in order for the matcher to work.
        /// </remarks>
        /// <param name="bytes_per_addr">How many bytes should be used to encode each address. See remarks.</param>
        /// <returns>Matcher value, which can be used to determine if any random address was included in this matcher's set.</returns>
        public List<byte[]> generateHiddenMatchAddresses(int bytes_per_addr)
        {
            List<byte[]> matchers = new List<byte[]>();
            byte[] matcher = new byte[48];
            byte[] filled = new byte[48];
            for (int i = 0; i < filled.Length; i++) { filled[i] = 0; }
            filled[0] = 1; // Never use the first byte for this
            // Start with initial random state
            if (randomSeedValue != 0)
            {
                rng = new Random(randomSeedValue);
            }
            rng.NextBytes(matcher);

            foreach (var addr in addresses)
            {
                if (filled.Count(x => x == 0) < bytes_per_addr)
                {
                    // Matcher is full
                    matchers.Add(matcher);
                    matcher = new byte[48];
                    rng.NextBytes(matcher);
                    for (int i = 0; i < filled.Length; i++) { filled[i] = 0; }
                    filled[0] = 1;
                }
                for (int j = 0; j < bytes_per_addr; j++)
                {
                    int b = getRandomUnfilledByte(filled);
                    matcher[b] = addr[b];
                    filled[b] = 1;
                }
            }
            // Add last matcher, if meaningful
            if (filled.Count(x => x > 0) > 1)
            {
                matchers.Add(matcher);
            }
            return matchers;
        }

        /// <summary>
        ///  Checks if the matcher contains the given address.
        /// </summary>
        /// <param name="addr">Address to search</param>
        /// <returns>True, if the matcher already contains this address.</returns>
        public bool containsAddress(byte[] addr)
        {
            return addresses.Any(x => x.SequenceEqual(addr));
        }
    }
}
