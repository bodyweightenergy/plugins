using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Rust;
using Newtonsoft.Json.Linq;

namespace Oxide.Plugins
{
    [Info("Government", "BodyweightEnergy", "0.0.1", ResourceId = 0)]
    public class Governments : RustPlugin
    {
        #region Plugin Private Members

        private static string GovernmentDataFilename = "GovernmentData";
        private static Dictionary<string, Government> govs;
        private static Dictionary<string, Government> lookup;
        private static Dictionary<string, string> originalNames;
        private static Dictionary<string, List<string>> permissionList;
        private static List<string> RankList;
        private static Dictionary<string, string> assignPermAssociation = new Dictionary<string, string> 
        {
            {"DICTATOR","modify_dictator"},
            {"HEAD", "modify_head"},
            {"BOOT", "modify_boot"},
            {"CITIZEN", "modify_citizen"}
        };

        #endregion

        #region Data Management

        public void SaveData()
        {
            var data = Interface.GetMod().DataFileSystem.GetDatafile(GovernmentDataFilename);
            // Saving Rank List Data
            var rankData = new List<object>();
            foreach (var rank in RankList) rankData.Add(rank);
            data["ranks"] = rankData;

            // Saving Permission Data
            var permissionsData = new Dictionary<string, object>();
            foreach (var permission in permissionList)
            {
                var permitList = new List<object>();
                foreach (var rank in permission.Value) permitList.Add(rank);
                permissionsData.Add(permission.Key, permitList);
            }
            data["permissions"] = permissionsData;

            // Saving Government Data
            var govsData = new Dictionary<string, object>();
            foreach (var gov in govs)
            {
                var govData = new Dictionary<string, object>();
                govData.Add("name", gov.Value.Name);
                var members = new Dictionary<string, object>();
                foreach (var imember in gov.Value.Members)
                    members.Add(imember.Key, imember.Value);
                var guests = new List<object>();
                foreach (var iguest in gov.Value.Guests)
                    guests.Add(iguest);
                var inviteds = new List<object>();
                foreach (var iinvited in gov.Value.Inviteds)
                    inviteds.Add(iinvited);
                govData.Add("members", members);
                govData.Add("guests", guests);
                govData.Add("inviteds", inviteds);
                govsData.Add(gov.Key, govData);
            }
            data["governments"] = govsData;
            Interface.GetMod().DataFileSystem.SaveDatafile(GovernmentDataFilename);
        }

        public void LoadData()
        {
            govs.Clear();
            var data = Interface.GetMod().DataFileSystem.GetDatafile(GovernmentDataFilename);

            // Load Rank List
            if (data["ranks"] != null)
            {
                var rankList = (List<object>)Convert.ChangeType(data["ranks"], typeof(List<object>));
                foreach (var irank in rankList) RankList.Add((string)irank);
            }

            // Load Permissions List
            if (data["permissions"] != null)
            {
                var permissionData = (Dictionary<string, object>)Convert.ChangeType(data["permissions"], typeof(Dictionary<string, object>));
                foreach (var ipermission in permissionData)
                {
                    var permitList = (List<object>)Convert.ChangeType(ipermission.Value, typeof(List<object>));
                    var newPermitList = new List<string>();
                    foreach (var permit in permitList) newPermitList.Add((string)permit);
                    permissionList.Add(ipermission.Key, newPermitList);
                }
            }

            // Load Government Data
            if (data["governments"] != null)
            {
                var govsData = (Dictionary<string, object>)Convert.ChangeType(data["governments"], typeof(Dictionary<string, object>));
                foreach (var igov in govsData)
                {
                    var gov = (Dictionary<string, object>)igov.Value;
                    var tag = (string)igov.Key;
                    var name = (string)gov["name"];
                    var membersData = (Dictionary<string, object>)gov["members"];
                    var members = new Dictionary<string, string>();
                    foreach (var imember in membersData)
                    {
                        var memberID = (string) imember.Key;
                        var memberRank = (string) imember.Value;
                        members.Add(memberID, memberRank);
                    }
                    var guestsData = (List<object>)gov["guests"];
                    var guests = new List<string>();
                    foreach (var iguest in guestsData)
                    {
                        guests.Add(iguest.ToString());
                    }
                    var invitedsData = (List<object>)gov["inviteds"];
                    var inviteds = new List<string>();
                    foreach (var iinvited in invitedsData)
                    {
                        inviteds.Add(iinvited.ToString());
                    }
                    var newGov = new Government() { Tag = tag, Name = name, Members = members, Guests = guests, Inviteds = inviteds };
                    govs.Add(tag, newGov);
                }
            }

        }


        #endregion //Data Management

        #region Government Class Methods

        private bool govNameExists(string name)
        {
            var exists = false;
            foreach (var gov in govs)
            {
                if (gov.Value.Name == name)
                {
                    exists = true;
                }
            }
            return exists;
        }
        public static string Rank(string rank_str)
        {
            if (RankList.Contains(rank_str))
            {
                return rank_str;
            }
            throw (new Exception("Attempted to assign invalid rank."));
            return null;
        }
        public Government getGovByUserID (string playerId)
        {
            if (govs.ContainsKey(playerId))
            {
                return govs[playerId];
            }
            return null;
        }
        private string GetRank(string playerId)
        {
            return lookup[playerId].Members[playerId];
        }
        public bool isMemberOfGov (string playerId, string tag)
        {
            return (govs[tag].isMember(playerId));
        }
        public bool isGuestOfGov (string playerId, string tag)
        {
            return (govs[tag].isGuest(playerId));
        }
        public bool isInvitedOfGov(string playerId, string tag)
        {
            return (govs[tag].isInvited(playerId));
        }

        private BasePlayer FindPlayerByPartialName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            BasePlayer player = null;
            name = name.ToLower();
            var allPlayers = BasePlayer.activePlayerList.ToArray();
            // Try to find an exact match first
            foreach (var p in allPlayers)
            {
                if (p.displayName == name)
                {
                    if (player != null)
                        return null; // Not unique
                    player = p;
                }
            }
            if (player != null)
                return player;
            // Otherwise try to find a partial match
            foreach (var p in allPlayers)
            {
                if (p.displayName.ToLower().IndexOf(name) >= 0)
                {
                    if (player != null)
                        return null; // Not unique
                    player = p;
                }
            }
            return player;
        }
        private string StripTag(string name, Government gov)
        {
            if (gov == null)
                return name;
            var re = new Regex(@"^\[" + gov.Tag + @"\]\s");
            while (re.IsMatch(name))
                name = name.Substring(gov.Tag.Length + 3);

            Puts("StripTag result = " + name);
            return name;
        }
        public static string FindPlayerNameByID(string playerId)
        {
            var player = BasePlayer.FindByID(Convert.ToUInt64(playerId));
            if (player == null) return "NULL";
            else return player.displayName;
        }

        private bool HasPermission(string playerId, string permissionType)
        {
            if (lookup[playerId] != null)
            {
                var memberRank = lookup[playerId].Members[playerId];
                if (permissionList[permissionType] != null)
                {
                    if (permissionList[permissionType].Contains(memberRank))
                    {
                        return true;
                        Puts("Player " + playerId + " has permission to " + permissionType + ".");
                    }
                }
            }
            return false;
        }

        private void SetupPlayer(BasePlayer player)
        {
            var prevName = player.displayName;
            var playerId = player.userID.ToString();
            var gov = getGovByUserID(playerId);
            player.displayName = StripTag(player.displayName, gov);
            if (gov == null)
            {
                return;
            }
            else
            {
                var tag = "[" + gov.Tag + "] ";
                if (!player.displayName.StartsWith(tag))
                    player.displayName = tag + prevName;
            }
            if (player.displayName != prevName)
                player.SendNetworkUpdate();
        }
        private void SetupPlayers(List<string> playerIds)
        {
            foreach (var playerId in playerIds)
            {
                var uid = Convert.ToUInt64(playerId);
                var player = BasePlayer.FindByID(uid);
                if (player != null)
                    SetupPlayer(player);
                else
                {
                    player = BasePlayer.FindSleeping(uid);
                    if (player != null)
                        SetupPlayer(player);
                }
            }
        }

        private void CreateGovernment(string tag, string name, string creatorID)
        {
            var newGov = new Government() { Tag = tag, Name = name };
            govs.Add(tag, newGov);
            newGov.AddMember(creatorID, Rank("DICTATOR"));
        }
        private void DisbandGovernment (string tag)
        {
            if(govs.ContainsKey(tag))
            {
                govs.Remove(tag);
            }
        }

        #endregion

        #region Hook Methods

        [HookMethod("OnServerInitialized")]
        private void OnServerInitialized()
        {
            try
            {
                lookup = new Dictionary<string, Government>();
                govs = new Dictionary<string, Government>();
                LoadData();
            }
            catch (Exception ex)
            {
                Error("OnServerInitialized failed", ex);
            }
        }

        #endregion

        #region Chat Commands

        [ChatCommand("gov_create")]
        private void cmdChatGovCreate(BasePlayer player, string command, string[] args)
        {
            var sb = new StringBuilder();
            var playerId = player.userID.ToString();
            if(args.Length != 2)
            {
                sb.Append("Invalid command. Type /gov_help for more info.");
            }
            else if (lookup[playerId] != null)
            {
                sb.Append("You are already a member of a government.");
            }
            else if (govs.ContainsKey(args[0]))
            {
                sb.Append("This tag has already been taken. Try another tag.");
            }
            else if (govNameExists(args[1]))
            {
                sb.Append("This name has already been taken. Try another name.");
            }
            else
            {
                CreateGovernment(args[0], args[1], player.userID.ToString());
                sb.Append("You have successfully created the " + args[1] + " government, and you are the dictator of it.");
            }
            SendReply(player, sb.ToString());
            SaveData();
        }

        [ChatCommand("gov_invite")]
        private void cmdChatGovInvite(BasePlayer player, string command, string[] args)
        {
            var sb = new StringBuilder();
            var playerId = player.userID.ToString();
            if (args.Length != 1)
            {
                sb.Append("Invalid command. Type /gov_help for more info.");
            }
            else if (!lookup.ContainsKey(playerId))
            {
                sb.Append("You are not a member of any government.");
            }
            else if (!HasPermission(playerId, "modify_citizen"))
            {
                sb.Append("You do not have permission to invite.");
            }
            else
            {
                var invitedPlayer = FindPlayerByPartialName(args[0]);
                var invitedID = invitedPlayer.userID.ToString();
                var invitingGov = lookup[playerId];
                invitingGov.Inviteds.Add(invitedID);
                sb.Append("You have invited " + invitedPlayer.displayName + " to join your domain.");
                sb.Append(" They must leave their existing government first (if they are a member of one)");
                sb.Append(", then type \"/gov_join " + invitingGov.Tag + "\" to join yours.");
            }
            SendReply(player, sb.ToString());
            SaveData();
        }

        [ChatCommand("gov_kick")]
        private void cmdChatGovKick(BasePlayer player, string command, string[] args)
        {
            var sb = new StringBuilder();
            var playerId = player.userID.ToString();
            if (args.Length != 1)
            {
                sb.Append("Invalid command. Type /gov_help for more info.");
            }
            else if (!lookup.ContainsKey(playerId))
            {
                sb.Append("You are not a member of any government.");
            }
            else
            {
                var kickedPlayer = FindPlayerByPartialName(args[0]);
                var kickedPlayerName = kickedPlayer.displayName;
                var kickedPlayerID = kickedPlayer.userID.ToString();
                if (kickedPlayer == null) sb.Append("Player name does not exist or isn't unique.");
                else if (!lookup.ContainsKey(kickedPlayerID)) sb.Append("This player is not a member of your domain.");
                else if (lookup[kickedPlayerID] != lookup[playerId]) sb.Append("This player is not a member of your domain.");
                else
                {
                    var kickedPlayerRank = GetRank(kickedPlayerID);
                    var permission = assignPermAssociation[kickedPlayerRank];
                    if (!HasPermission(playerId, permission))
                    {
                        sb.Append("You do not have permission to kick this player.");
                    }
                    else
                    {
                        lookup[playerId].RemoveMember(kickedPlayerID);
                        sb.Append("You have successfully kicked " + kickedPlayerName + " from your government.");
                        lookup[playerId].Broadcast(kickedPlayerName + " has been banished from the " + lookup[playerId].Name + " government.");
                    }
                }
            }
            SendReply(player, sb.ToString());
            SaveData();
        }

        [ChatCommand("gov_assign_rank")]
        private void cmdChatGovAssignRank(BasePlayer player, string command, string[] args)
        {
            var sb = new StringBuilder();
            var playerId = player.userID.ToString();
            if (args.Length != 2)
            {
                sb.Append("Invalid command. Type /gov_help for more info.");
            }
            else if (!lookup.ContainsKey(playerId))
            {
                sb.Append("You are not a member of any government.");
            }
            else
            {
                var assignedPlayer = FindPlayerByPartialName(args[0]).userID.ToString();
                var assignedPlayerName = FindPlayerByPartialName(args[0]).displayName;
                var assignedRank = Rank(args[1]);
                if (assignedPlayer == null) sb.Append("Player name does not exist or is not unique.");
                else if (lookup[assignedPlayer] != lookup[playerId]) sb.Append("The player is not a member of your government.");
                else if (!HasPermission(playerId, assignPermAssociation[assignedRank])) sb.Append("You do not have permission to " + assignPermAssociation[assignedRank] + ".");
                else
                {
                    lookup[playerId].Members[assignedPlayer] = assignedRank;
                    sb.Append(assignedPlayerName + " has been successfully assigned as " + assignedRank + ".");
                }

            }
            SendReply(player, sb.ToString());
            SaveData();
        }

        [ChatCommand("gov_join")]
        private void cmdChatGovJoin(BasePlayer player, string command, string[] args)
        {
            var sb = new StringBuilder();
            var playerId = player.userID.ToString();
            if (args.Length != 1)
            {
                sb.Append("Invalid command. Type /gov_help for more info.");
            }
            else if (lookup.ContainsKey(playerId))
            {
                sb.Append("To join another government, you must leave this one first by typing");
                sb.Append(" \"/gov_leave\".");
            }
            else if(!govs.ContainsKey(args[0]))
            {
                sb.Append("No such government exists.");
            }
            else if (!govs[args[0]].isInvited(playerId))
            {
                sb.Append("You were not invited to join this government. Make sure a permitted member of that government has already invited you.");
            }
            else
            {
                var invitingGov = govs[args[0]];
                invitingGov.AddMember(playerId, Rank("CITIZEN"));
                invitingGov.Inviteds.Remove(playerId);
                sb.Append("You are now a " + GetRank(playerId) + " of the " + lookup[playerId].Name + " government.");
            }
            SendReply(player, sb.ToString());
            SaveData();
        }

        [ChatCommand("gov_leave")]
        private void cmdChatGovLeave(BasePlayer player, string command, string[] args)
        {
            var sb = new StringBuilder();
            var playerId = player.userID.ToString();
            if (args.Length != 1)
            {
                sb.Append("Invalid command. Type /gov_help for more info.");
            }
            else if (!lookup.ContainsKey(playerId))
            {
                sb.Append("You are not a member of any governments.");
            }
            else
            {
                var leftGov = lookup[playerId];
                leftGov.RemoveMember(playerId);
                sb.Append("You are no longer a member of the " + leftGov.Name + " government.");
                leftGov.Broadcast(FindPlayerNameByID(playerId) + " has left the " + leftGov.Name + " government.");
            }
            SendReply(player, sb.ToString());
            SaveData();
        }

        [ChatCommand("gov_info")]
        private void cmdChatGovInfo(BasePlayer player, string command, string[] args)
        {
            var sb = new StringBuilder();
            var playerId = player.userID.ToString();
            if (args.Length != 0)
            {
                sb.Append("Invalid command. Type /gov_help for more info.");
            }
            else if(!lookup.ContainsKey(playerId))
            {
                sb.Append("You are not a member of any government.");
            }
            else
            {
                var gov = lookup[playerId];
                sb.Append("Your Government's Info:\n");
                sb.Append("[" + gov.Tag + "] " + gov.Name + "\n");
                var sortedList = gov.GetSortedMemberList();
                foreach (var member in sortedList)
                {
                    sb.Append(member.Key + "\t" + member.Value + "\t" + FindPlayerNameByID(member.Key) + "\n");
                }
                        
            }
            SendReply(player, sb.ToString());
            SaveData();
        }

        #endregion

        #region class Government

        public class Government
        {
            // Private Members
            public string Tag { get; set; }
            public string Name { get; set; }
            public Dictionary<string, string> Members { get; set; }
            public List<string> Guests { get; set; }
            public List<string> Inviteds { get; set; }
            // Properties
            public string Dictator
            {
                get
                {
                    foreach (var member in Members)
                    {
                        if(member.Value == "DICTATOR")
                        {
                            return member.Key;
                        }
                    }
                    return null;
                }
                set
                {
                    var newCrownId = value;
                    bool crownExists = false;
                    foreach(var member in Members)
                    {
                        if (member.Value == "DICTATOR")
                        {
                            crownExists = true;
                        }
                    }
                    if(!crownExists)
                    {
                        Members[newCrownId] = "DICTATOR";
                    }
                }
            }
            // Methods
            public bool isMember(string playerId)
            {
                return (Members.ContainsKey(playerId) && lookup.ContainsKey(playerId));
            }
            public bool isGuest(string playerId)
            {
                return (Guests.Contains(playerId));
            }
            public bool isInvited (string playerId)
            {
                return (Inviteds.Contains(playerId));
            }

            public void AddMember(string playerId, string rank)
            {
                if (!isMember(playerId) || !lookup.ContainsKey(playerId))
                {
                    Members.Add(playerId, Rank(rank));
                    lookup.Add(playerId, this);
                }
            }
            public void ModifyMember(string playerId, string rank)
            {
                if (isMember(playerId) && lookup.ContainsKey(playerId))
                {
                    Members[playerId] = rank;
                }
            }
            public void RemoveMember (string playerId)
            {
                if (isMember(playerId) && lookup.ContainsKey(playerId))
                {
                    Members.Remove(playerId);
                    lookup.Remove(playerId);
                }
            }

            public List<KeyValuePair<string,string>> GetSortedMemberList()
            {
                List<KeyValuePair<string, string>> myList = Members.ToList();

                myList.Sort((firstPair, nextPair) =>
                {
                    return firstPair.Value.CompareTo(nextPair.Value);
                }
                );
                return myList;
            }

            public void Broadcast(string message)
            {
                string message_header = "<color=#a1ff46>(GOV BROADCAST)</color> ";

                // Send message to members
                foreach (var member in Members)
                {
                    var player = BasePlayer.FindByID(Convert.ToUInt64(member.Key));
                    if (player == null)
                        continue;
                    player.SendConsoleCommand("chat.add", "", message_header + message);
                }

            }
        }

        #endregion

        #region Utility Methods

        private void Log(string message) {
            Interface.Oxide.LogInfo("{0}: {1}", Title, message);
        }

        private void Warn(string message) {
            Interface.Oxide.LogWarning("{0}: {1}", Title, message);
        }

        private void Error(string message, Exception ex = null) {
            if (ex != null)
                Interface.Oxide.LogException(string.Format("{0}: {1}", Title, message), ex);
            else
                Interface.Oxide.LogError("{0}: {1}", Title, message);
        }

        #endregion
    }
}
