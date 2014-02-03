using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

#if NETFX_CORE
using NetworkCommsDotNet.Tools.XPlatformHelper;
#endif

namespace NetworkCommsDotNet.Tools
{
    /// <summary>
    /// NetworkComms.Net class used for providing Denial Of Service (DOS) protection features. 
    /// If enabled, malformed data events and connection initialises are tracked. If above
    /// set thresholds IPAddresses are banned.
    /// </summary>
    public class DOSProtection
    {
        /// <summary>
        /// A local thread safety locker
        /// </summary>
        object _syncRoot = new object();

        /// <summary>
        /// Addresses that are currently banned. Key is remote IPAddress, value is time banned.
        /// </summary>
        Dictionary<IPAddress, DateTime> _bannedAddresses = new Dictionary<IPAddress, DateTime>();

        /// <summary>
        /// First key is remote IPAddress, second key is DateTime.Ticks, value is the malformed count for that DateTime.ticks
        /// </summary>
        Dictionary<IPAddress, Dictionary<long, int>> _malformedCountDict = new Dictionary<IPAddress, Dictionary<long, int>>();

        /// <summary>
        /// First key is remote IPAddress, second key is DateTime.Ticks, value is the connection initialisation count for that DateTime.ticks
        /// </summary>
        Dictionary<IPAddress, Dictionary<long, int>> _connectionInitialiseCountDict = new Dictionary<IPAddress, Dictionary<long, int>>();

        /// <summary>
        /// The current state of DOS protection
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// The timeout after which time a banned IPAddress may have access again. Default is 10 minutes.
        /// </summary>
        public TimeSpan BanTimeout { get; set; }

        /// <summary>
        /// The time within which if MalformedCountInIntervalBeforeBan or ConnectionInitialiseCountInIntervalBeforeBan
        /// is reached a peer will be banned. Default is 5 minutes. 
        /// </summary>
        public TimeSpan LogInterval { get; set; }

        /// <summary>
        /// The number of malformed data counts to log within LogInterval before an IPAddress is banned. Default is 2.
        /// </summary>
        public int MalformedCountInIntervalBeforeBan { get; set; }

        /// <summary>
        /// The number of connection initialises to log within LogInterval before an IPAddress is banned. Default is 100
        /// </summary>
        public int ConnectionInitialiseCountInIntervalBeforeBan { get; set; }

        /// <summary>
        /// Initialise a new instance of DOS protection.
        /// </summary>
        public DOSProtection()
        {
            Enabled = false;
            BanTimeout = new TimeSpan(0, 10, 0);
            LogInterval = new TimeSpan(0, 5, 0);
            MalformedCountInIntervalBeforeBan = 2;
            ConnectionInitialiseCountInIntervalBeforeBan = 100;
        }

        /// <summary>
        /// Log a malformed data event for the provided remote IPAddress.
        /// </summary>
        /// <param name="remoteIPAddress"></param>
        /// <returns>True if the remote IPAddress is now banned, otherwise false.</returns>
        public bool LogMalformedData(IPAddress remoteIPAddress)
        {
            bool ipAddressNowBanned = false;

            lock (_syncRoot)
            {
                //Record the malformed data count
                long tick = DateTime.Now.Ticks;
                if (_malformedCountDict.ContainsKey(remoteIPAddress))
                {
                    if (_malformedCountDict[remoteIPAddress].ContainsKey(tick))
                        _malformedCountDict[remoteIPAddress][tick]++;
                    else
                        _malformedCountDict[remoteIPAddress].Add(tick, 1);
                }
                else
                    _malformedCountDict.Add(remoteIPAddress, new Dictionary<long, int>() { { tick, 1 } });

                //Delete any tick keys which are greater than LogInterval
                List<long> existingIPAddressTickKeys = new List<long>(_malformedCountDict[remoteIPAddress].Keys);

                //Sort from oldest to newest
                existingIPAddressTickKeys.Sort();

                //Keep removing tick keys until we are within LogInterval
                for (int i = 0; i < existingIPAddressTickKeys.Count; i++)
                {
                    if (DateTime.Now - new DateTime(existingIPAddressTickKeys[i]) > LogInterval)
                        _malformedCountDict[remoteIPAddress].Remove(existingIPAddressTickKeys[i]);
                    else
                        break;
                }

                //Add up the remaining counts and see if we need to ban this peer
                int currentMalformedCount = 0;
                foreach(int count in _malformedCountDict[remoteIPAddress].Values)
                    currentMalformedCount += count;

                if (currentMalformedCount >= MalformedCountInIntervalBeforeBan)
                {
                    ipAddressNowBanned = true;
                    _bannedAddresses[remoteIPAddress] = new DateTime(tick);
                }
                else
                {
                    ipAddressNowBanned = false;
                    _bannedAddresses.Remove(remoteIPAddress);
                }

                //Remove the remote IPAddress key if no events are left
                if (_malformedCountDict[remoteIPAddress].Count == 0)
                    _malformedCountDict.Remove(remoteIPAddress);
            }

            return ipAddressNowBanned;
        }

        /// <summary>
        /// Log a connection initialisation for the provided remote IPAddress. 
        /// </summary>
        /// <param name="remoteIPAddress"></param>
        /// <returns>True if the remote IPAddress is now banned, otherwise false.</returns>
        public bool LogConnectionInitialise(IPAddress remoteIPAddress)
        {
            bool ipAddressNowBanned = false;

            lock (_syncRoot)
            {
                //Record the malformed data count
                long tick = DateTime.Now.Ticks;
                if (_connectionInitialiseCountDict.ContainsKey(remoteIPAddress))
                {
                    if (_connectionInitialiseCountDict[remoteIPAddress].ContainsKey(tick))
                        _connectionInitialiseCountDict[remoteIPAddress][tick]++;
                    else
                        _connectionInitialiseCountDict[remoteIPAddress].Add(tick, 1);
                }
                else
                    _connectionInitialiseCountDict.Add(remoteIPAddress, new Dictionary<long, int>() { { tick, 1 } });

                //Delete any tick keys which are greater than LogInterval
                List<long> existingIPAddressTickKeys = new List<long>(_connectionInitialiseCountDict[remoteIPAddress].Keys);

                //Sort from oldest to newest
                existingIPAddressTickKeys.Sort();

                //Keep removing tick keys until we are within LogInterval
                for (int i = 0; i < existingIPAddressTickKeys.Count; i++)
                {
                    if (DateTime.Now - new DateTime(existingIPAddressTickKeys[i]) > LogInterval)
                        _connectionInitialiseCountDict[remoteIPAddress].Remove(existingIPAddressTickKeys[i]);
                    else
                        break;
                }

                //Add up the remaining counts and see if we need to ban this peer
                int currentConnectionInitialisationCount = 0;
                foreach (int count in _connectionInitialiseCountDict[remoteIPAddress].Values)
                    currentConnectionInitialisationCount += count;

                //Make a decision based on currentConnectionInitialisationCount
                if (currentConnectionInitialisationCount >= ConnectionInitialiseCountInIntervalBeforeBan)
                {
                    ipAddressNowBanned = true;
                    _bannedAddresses[remoteIPAddress] = new DateTime(tick);
                }
                else
                {
                    ipAddressNowBanned = false;
                    _bannedAddresses.Remove(remoteIPAddress);
                }

                //Remove the remote IPAddress key if no events are left
                if (_connectionInitialiseCountDict[remoteIPAddress].Count == 0)
                    _connectionInitialiseCountDict.Remove(remoteIPAddress);
            }

            return ipAddressNowBanned;
        }

        /// <summary>
        /// Returns true if the provided IPAddress has been banned due to DOSProtection.
        /// </summary>
        /// <param name="remoteIPAddress">The IPAddress to check</param>
        /// <returns></returns>
        public bool RemoteIPAddressBanned(IPAddress remoteIPAddress)
        {
            lock(_syncRoot)
            {
                if (_bannedAddresses.ContainsKey(remoteIPAddress))
                {
                    //If the ban time is longer than the timeout we can allow it again
                    if (DateTime.Now - _bannedAddresses[remoteIPAddress] > BanTimeout)
                    {
                        _bannedAddresses.Remove(remoteIPAddress);
                        return false;
                    }
                    else
                        return true;
                }
                else
                    return false;
            }
        }
    }
}
