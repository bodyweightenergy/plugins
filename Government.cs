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
    public class Government : RustPlugin
    {
        public static List<string> RankList = new List<string>();
        private bool isServerInitialized = false;
        private bool isLoaded = false;

        public static string dataFilename = "government_datafile";
        public static Dictionary<string, Domain> lookup;     //contains <playerId, Domain>
        public static Dictionary<string, Domain> domains;    //contains <domainTag, Domain>
        public static Dictionary<string, List<string>> permissions = new Dictionary<string,List<string>>(); //contains < permissionName, List<ranks> >
        private Regex tagRe = new Regex("^[a-zA-Z0-9]{2,6}$");
        private static string json = @"
		[{
			""name"":   ""GovInfoGUI"",
			""parent"": ""Overlay"",
			""components"":
			[
				{
					""type"":	   ""UnityEngine.UI.Button"",
					""color"":	   ""%background%"",
					""imagetype"": ""Tiled""
				},
				{
					""type"":	   ""RectTransform"",
					""anchormin"": ""%left% %bottom%"",
					""anchormax"": ""%right% %top%""
				} 
			]
		},
		{ 
			""parent"": ""GovInfoGUI"",
			""components"":
			[
				{
					""type"":	  ""UnityEngine.UI.Text"",
					""text"":	  ""%domain_info%"",
					""fontSize"": %size%,
					""color"":    ""%color%"",
					""align"":    ""MiddleLeft""
				}
			]
		}]";

        // Saves the data file
        public void SaveData()
        {
            var data = Interface.GetMod().DataFileSystem.GetDatafile(dataFilename);
            // Saving Rank List Data
            var rankData = new List<object>();
            foreach (var rank in RankList) rankData.Add(rank);
            data["ranks"] = rankData;
            // Saving Permission Data
            var permissionsData = new Dictionary<string, object>(); 
            foreach (var permission in permissions)
            {
                var permitList = new List<object>();
                foreach (var rank in permission.Value) permitList.Add(rank);
                permissionsData.Add(permission.Key, permitList);
            }
            data["permissions"] = permissionsData;
            // Saving Domain Data
            var domainsData = new Dictionary<string,object>();
            foreach (var domain in domains)
            {
                var domainData = new Dictionary<string,object>();
                domainData.Add("name", domain.Value.Name);
                var members = new Dictionary<string,object>();
                foreach (var imember in domain.Value.Members) 
                {
                    var memberData = new Dictionary<string, object>();
                    memberData.Add("playerRank", imember.Rank);
                    memberData.Add("playerNotes", imember.PlayerNotes);
                    members.Add(imember.UserID, memberData);
                }
                var guests = new List<object>();
                foreach (var iguest in domain.Value.Guests)
                    guests.Add(iguest);
                var inviteds = new List<object>();
                foreach (var iinvited in domain.Value.Inviteds)
                    inviteds.Add(iinvited);
                domainData.Add("members", members);
                domainData.Add("guests", guests);
                domainData.Add("inviteds", inviteds);
                domainsData.Add(domain.Value.Tag, domainData);
            }
            data["domains"] = domainsData;
            Interface.GetMod().DataFileSystem.SaveDatafile(dataFilename);
        }
        // Loads the data file
        public void LoadData()
        {
            domains.Clear();
            var data = Interface.GetMod().DataFileSystem.GetDatafile(dataFilename);
            // Load Rank List
            if (data["ranks"] != null)
            {
                var rankList = (List<object>) Convert.ChangeType(data["ranks"], typeof(List<object>));
                foreach (var irank in rankList) RankList.Add((string)irank);
            }
            if (data["permissions"] != null)
            {
                var permissionData = (Dictionary<string, object>) Convert.ChangeType(data["permissions"], typeof(Dictionary<string, object>));
                foreach (var ipermission in permissionData)
                {
                    var permitList = (List<object>) Convert.ChangeType(ipermission.Value, typeof(List<object>));
                    var newPermitList = new List<string>();
                    foreach (var permit in permitList) newPermitList.Add((string)permit);
                    permissions.Add(ipermission.Key, newPermitList);
                }
            }
            // Load Domain data
            if (data["domains"] != null)
            {
                var domainsData = (Dictionary<string,object>) Convert.ChangeType(data["domains"], typeof(Dictionary<string,object>));
                foreach (var idomain in domainsData) 
                {
                    var domain = (Dictionary<string,object>) idomain.Value;
                    var tag = (string) idomain.Key;
                    var name = (string)domain["name"];
                    var membersData = (Dictionary<string,object>) domain["members"];
                    var members = new List<Member>();
                    foreach (var imemberData in membersData)
                    {
                        var memberData = (Dictionary<string, object>) imemberData.Value;
                        var member = new Member() { UserID = imemberData.Key.ToString(), Rank = memberData["playerRank"].ToString(), PlayerNotes = memberData["playerNotes"].ToString() };
                        members.Add(member);
                    }
                    var guestsData = (List<object>)domain["guests"];
                    var guests = new List<string>();
                    foreach (var iguestData in guestsData)
                    {
                        guests.Add(iguestData.ToString());
                    }
                    var invitedsData = (List<object>)domain["inviteds"];
                    var inviteds = new List<string>();
                    foreach (var iinvitedData in invitedsData)
                    {
                        inviteds.Add(iinvitedData.ToString());
                    }
                    var newDomain = new Domain(tag, name);
                    foreach (var member in members)
                    {
                        newDomain.AddMember(member.UserID);
                        newDomain.ChangeMemberRank(member.UserID, member.Rank);
                        newDomain.AssignPlayerNotes(member.UserID, member.PlayerNotes);
                    }
                    newDomain.Guests = guests;
                    newDomain.Inviteds = inviteds;
                }
                Puts("Datafile loaded ({0}) domains successfully.", domains.Count);
            }
        }
        // Load GUI
        public void Load()  
        {
            CheckCreateConfig();

            double left = (double)Config["Position", "Left"];
            double right = (double)Config["Position", "Left"] + (double)Config["Size", "Width"];
            double bottom = (double)Config["Position", "Bottom"];
            double top = (double)Config["Position", "Bottom"] + (double)Config["Size", "Height"];

            json = json.Replace("%background%", (string)Config["BackgroundColor"])
                       .Replace("%color%", (string)Config["TextColor"])
                       .Replace("%size%", Config["FontSize"].ToString())
                       .Replace("%left%", left.ToString())
                       .Replace("%right%", right.ToString())
                       .Replace("%bottom%", bottom.ToString())
                       .Replace("%top%", top.ToString());
            UpdateGUI();
        }
        public void Unload()
        {
            DestroyGUI();
        }
        // Finds a player by partial name
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
        // Finds a domain by tag
        private Domain FindDomain(string tag)
        {
            Domain domain;
            if (domains.TryGetValue(tag, out domain))
                return domain;
            return null;
        }
        
        // Finds a user's domain
        private Domain FindDomainByUser(string userId)
        {
            Domain domain;
            if (lookup.TryGetValue(userId, out domain))
                return domain;
            return null;
        }
        // Strips the tag from a player's name
        private string StripTag(string name, Domain domain)
        {
            if (domain == null)
                return name;
            var re = new Regex(@"^\[" + domain.Tag + @"\]\s");
            while (re.IsMatch(name))
                name = name.Substring(domain.Tag.Length + 3);

            Puts("StripTag result = " + name);
            return name;
        }

        // Sets up a player to use the correct domain tag
        private void SetupPlayer(BasePlayer player)
        {
            var prevName = player.displayName;
            var playerId = player.userID.ToString();
            var domain = FindDomainByUser(playerId);
            player.displayName = StripTag(player.displayName, domain);
            if (domain == null)
            {
                return;
            }
            else 
            {
                var tag = "[" + domain.Tag + "] "; 
                if (!player.displayName.StartsWith(tag))
                    player.displayName = tag + prevName;
            }
            if (player.displayName != prevName)
                player.SendNetworkUpdate();
        }
        // Sets up all players contained in playerIds
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
                var playerDomain = lookup[playerId];
                var member = playerDomain.FindMemberByID(playerId);
                var memberRank = member.Rank;
                if (permissions[permissionType] != null)
                {
                    if (permissions[permissionType].Contains(memberRank))
                    {
                        return true;
                        Puts("Player " + playerId + " has permission to " + permissionType + ".");
                    }
                }
            }
            return false;
        }

        private Domain FindPlayerDomain(string playerId)
        {
            if (lookup.ContainsKey(playerId))
            {
                return lookup[playerId];
            }
            return null;
        }
        public static string AssignRank(string rank)
        {

            if (RankList.Contains(rank))
            {
                return rank;
            }
            else
            {
                throw new Exception("Attempt to assign an invalid rank.");
                return null;
            }
        }
         
        public void AddGUI()
        {
            try
            {
                foreach (BasePlayer bp in BasePlayer.activePlayerList)
                {
                    var playerId = bp.userID.ToString();
                    if (lookup.ContainsKey(playerId))
                    {
                            if (lookup[playerId] != null)
                            {
                                var playerDomain = lookup[playerId];
                                var domainTag = playerDomain.Tag;
                                var domainName = playerDomain.Name;
                                var member = playerDomain.FindMemberByID(playerId);
                                var memberRank = member.Rank;
                                //var currentCrownName = BasePlayer.FindByID(Convert.ToUInt64(playerDomain.Crown.UserID)).displayName;
                                var guiString = "Domain: " + domainName + " [" + domainTag + "]\n" + /*"Crown: " + currentCrownName + */"\nYour Rank: " + memberRank;
                                //CommunityEntity.ServerInstance.ClientRPCEx(new Network.SendInfo() { connection = bp.net.connection }, null, "AddUI", json.Replace("%domain_info%", guiString));
                            }
                            else
                            {
                                var guiString = "You are not a member of any domain.";
                                CommunityEntity.ServerInstance.ClientRPCEx(new Network.SendInfo() { connection = bp.net.connection }, null, "AddUI", json.Replace("%domain_info%", guiString));
                            }
                    }
                }
            }
            catch (Exception ex)
            {
                Error("AddGUI failed ", ex);
            }
        }
        public void DestroyGUI()
        {
            foreach (BasePlayer p in BasePlayer.activePlayerList)
                CommunityEntity.ServerInstance.ClientRPCEx(new Network.SendInfo() { connection = p.net.connection }, null, "DestroyUI", "GovInfoGUI");
        }
         
        [HookMethod("OnServerInitialized")]
        private void OnServerInitialized()
        {
            try
            {
                lookup = new Dictionary<string, Domain>();
                domains = new Dictionary<string, Domain>();
                LoadData();
                Load();
                foreach (var player in BasePlayer.activePlayerList)
                    SetupPlayer(player);
                foreach (var player in BasePlayer.sleepingPlayerList)
                    SetupPlayer(player);
            }
            catch (Exception ex)
            {
                Error("OnServerInitialized failed", ex);
            }
        }

        [HookMethod("OnPlayerInit")]
        private void OnPlayerInit(BasePlayer player)
        {
            try
            {
                SetupPlayer(player);
            }
            catch (Exception ex)
            {
                Error("OnPlayerInit failed", ex);
            }
        }

        [HookMethod("OnPluginLoaded")]
        private void OnPluginLoaded()
        {
            Load();
        }

        [HookMethod("SaveConfig")]
        private void SaveConfig()
        {

        }

        // Main Chat-Plugin Interaction
        [ChatCommand("gov")]
        void cmdChatDomain(BasePlayer player, string command, string[] args)
        {
            Puts("Entered cmdChatDomain function.");
            string playerId = player.userID.ToString();
            var playerDomain = (lookup.ContainsKey(playerId) ? lookup[playerId] : null);
            var sb = new StringBuilder();
             
            if (args.Length == 0)
            {
                sb.Append("Your steamID is " + playerId + " and you're a " + playerDomain.GetMemberRank(player.userID.ToString()).ToString() + " to the " + (playerDomain != null ? playerDomain.Name : "null") + " Domain");
            }

            else
            {
                switch (args[0])
                {
                    case "create":
                        Puts("Entered create case.");
                        if (args.Length != 3)
                        {
                            sb.Append("Invalid command syntax. Use /gov create <domainTag> <domainName>");
                        }
                        else if (playerDomain != null)
                        {
                            sb.Append("You are already a member of a domain.");
                        }
                        else if (domains.ContainsKey(args[1]))
                        {
                            sb.Append("A domain with this tag already exists.");
                        }
                        else if (domains.ContainsKey(args[1]))
                        {
                            sb.Append("A domain with this tag already exists.");
                        }
                        else
                        {
                            var newDomain1 = new Domain(args[1], args[2], playerId);
                            sb.Append("You have successfully created the " + args[2] + " domain.");
                        }
                        SetupPlayer(player);
                        break;
                    case "join":
                        if (args.Length != 2)
                        {
                            sb.Append("Invalid command syntax. Use /gov join <domainTag>");
                        }
                        else if (playerDomain != null)
                        {
                            sb.Append("You are already a member of a domain.");
                        }
                        else if (!domains.ContainsKey(args[1]))
                        {
                            sb.Append("No such domain exists.");
                        }
                        else if (domains.ContainsKey(args[1]))
                        {
                            var joinedDomain = domains[args[1]];
                            if (!joinedDomain.IsInvited(playerId))
                            {
                                sb.Append("You cannot join a domain that you weren't invited to.");
                            }
                            else
                            {
                                joinedDomain.AddMember(playerId);
                                sb.Append("You have successfully joined the " + joinedDomain.Name + " domain.");
                            }
                        }
                        break;
                    case "invite":
                        var domain = lookup[playerId];
                        if (args.Length != 2)  sb.Append("Invalid command syntax. Use /gov invite <Full/Partial player name>");
                        else if (!HasPermission(playerId, "modify_citizen"))
                        {
                            sb.Append("You don't have permission to invite others to join your domain.");
                        }
                        else {
                            var invitedPlayer = FindPlayerByPartialName(args[1]);
                            if (invitedPlayer == null)
                            {
                                sb.Append("Player name either doesn't exist or isn't unique. Try the full name.");
                            }
                            else if (domain.Inviteds.Contains(invitedPlayer.userID.ToString()))
                            {
                                sb.Append("This player has already been invited to join your domain.");
                            }
                            else 
                            {
                                domain.Inviteds.Add(invitedPlayer.userID.ToString());
                                sb.Append("You successfully invited " + invitedPlayer.displayName + " to become a citizen in your domain.");
                            }
                        }
                        break;
                    case "leave":
                        Puts("Entered leave case.");
                        if (args.Length != 1)
                        {
                            sb.Append("Invalid command syntax. Use /gov leave");
                        }
                        else if (playerDomain == null)
                        {
                            sb.Append("You are not a member of any domain.");
                        }
                        else
                        {
                            playerDomain.RemoveMember(playerId);
                            sb.Append("You have successfully left the " + playerDomain.Name + " Domain.");
                        }
                        SetupPlayer(player);
                        break;
                    case "dump":
                        if (domains.Count == 0)
                        {
                            sb.Append("No domains in registry.");
                        }
                        else
                        {
                            foreach (var idomain in domains)
                            {
                                var dumpedDomain = idomain.Value; 
                                sb.Append(dumpedDomain.DumpData());
                            }
                        }
                        break;
                    default:
                        sb.Append("Error in command.");
                        break;
                }
            }
            SendReply(player, sb.ToString());
            SaveData();

        }

        #region Debug Methods
        [ConsoleCommand("gov.dump")]
        private void cmdDumpData(ConsoleSystem.Arg arg)
        {
            var sb = new StringBuilder();
            // Dump Domain data to console
            sb.Append("Available Domains:\n\n");
            if (domains.Count == 0)
            {
                sb.Append("No domains in registry.");
            }
            else
            {
                foreach (var domain in domains)
                {
                    var dumpedDomain = domain.Value;
                    sb.Append(dumpedDomain.DumpData());
                }
            }

            // Dump Rank List to cosole
            if (RankList != null)
            {
                sb.Append("\n\nAvailable ranks: [");
                foreach (var rank in RankList)
                {
                    sb.Append(rank + ", ");
                }
                sb.Append("]");
            }

            // Dump Permissions to console
            if (permissions != null)
            {
                sb.Append("\n\nAvailable permissions:\n\n");
                foreach (var permission in permissions)
                {
                    sb.Append(permission.Key + ": [");
                    foreach (var rank in permission.Value)
                    {
                        sb.Append(rank + ", ");
                    }
                    sb.Append("]\n");
                }
            }
            PrintToConsole(arg.Player(), sb.ToString());
        }

        [ConsoleCommand("gov.lookup")]
        private void cmdDumpLookup(ConsoleSystem.Arg arg)
        {
            var sb = new StringBuilder();
            if (lookup.Count == 0)
            {
                sb.Append("No players in registry.");
            }
            else foreach (var player in lookup)
                {
                    string domainName = "";
                    if (player.Value == null)
                    {
                        domainName = "NULL_DOMAIN";
                    }
                    else
                    {
                        domainName = player.Value.Name;
                    }
                    sb.Append(player.Key.ToString() + "\t" + domainName + "\n");
                }
            PrintToConsole(arg.Player(), sb.ToString());
        }

        [ConsoleCommand("gov.test")]
        private void cmdGovTest(ConsoleSystem.Arg arg)
        {
            var sb = new StringBuilder();
            if (arg.Args == null)
            {
                sb.Append("Invalid number of arguments.");
            }
            else if (arg.Args.Length == 0)
            {
                sb.Append("Invalid number of arguments.");
            }
            else
            {
                var args = arg.Args;
                var firstArg = args[0];
                switch (firstArg)
                {
                    case "cleardata":
                        domains.Clear();
                        lookup.Clear();
                        SaveData();
                        LoadData();
                        break;
                    case "createdomain":
                        if (args.Length != 3)
                        {
                            sb.Append("Correct syntax: gov.test createdomain <domainTag> <domainName>");
                        }
                        else if (domains.ContainsKey(args[1]))
                        {
                            sb.Append("Domain tag already exists. Choose another.");
                        }
                        else
                        {
                            var newDomain = new Domain(args[1], args[2]);
                            sb.Append("New domain created: [" + args[1] + "] " + args[2]);
                        }
                        break;
                    case "createplayer":
                        if (args.Length != 2 && args.Length != 3 && args.Length != 4)
                        {
                            sb.Append("Correct Syntax: gov.test createplayer <playerId> [<domainTag>]");
                        }
                        else if (args.Length == 2)
                        {
                            var playerId = args[1];
                            lookup.Add(playerId, null);
                            sb.Append("New domainless player " + playerId + " added.");
                        }
                        else
                        {
                            var playerId = args[1];
                            var domainTag = args[2];
                            if (!domains.ContainsKey(domainTag))
                            {
                                sb.Append("No such domain exists.");
                            }
                            else
                            {
                                var domain = domains[domainTag];
                                domain.AddMember(playerId);
                                if (args.Length == 4)
                                {
                                    domain.ChangeMemberRank(playerId, args[3]);
                                    sb.Append("Successfully added player " + playerId + " to the " + domain.Name + " domain as a " + args[3] +".");
                                }
                                else sb.Append("Successfully added player " + playerId + " to the " + domain.Name + " domain as a CITIZEN.");
                            }
                        }
                        break;
                    case "changerank":
                        if (args.Length != 3)
                        {
                            sb.Append("Correct Syntax: gov.test changerank <playerId> <rank>");
                        }
                        else
                        {
                            var playerId = args[1];
                            if (!lookup.ContainsKey(playerId))
                            {
                                sb.Append("Player with ID " + playerId + " was not found.");
                            }
                            else
                            {
                                if (lookup[playerId] == null)
                                {
                                    sb.Append("Cannot modify player's rank since he doesn't belong to any domain.");
                                }
                                else
                                {
                                    var playerDomain = lookup[playerId];
                                    playerDomain.ChangeMemberRank(playerId, args[2]);
                                    sb.Append("Player's rank successfully changed to " + args[2]);
                                }
                            }
                        }
                        break;
                    default:
                        sb.Append("Invalid command.");
                        break;
                }
            }
            PrintToConsole(arg.Player(), sb.ToString());
            SaveData();

        }
        #endregion

        // Represents a member 
        public class Member 
        {
            private string userID;
            private string playerRank;
            private string playerNotes;

            public string UserID
            {
                get { return userID; }
                set { userID = value; }
            }
            public string Rank
            {
                get { return playerRank; }
                set { playerRank = value; }
            }
            public string PlayerNotes
            {
                get { return playerNotes; }
                set { playerNotes = value; }
            }
        }

        // Represents a domain
        public class Domain
        {
            public Government mGovernment;
            private string name;
            private string tag;
            private List<Member> members;
            private List<string> guests;
            private List<string> inviteds;

            public string Name
            {
                get { return name; }
                set { name = value; }
            }
            public string Tag
            {
                get { return tag; }
                set { tag = value; }
            }
            public List<Member> Members
            {
                get { return members; }
                set { members = value; }
            }
            public List<string> Guests
            {
                get { return guests; }
                set { guests = value; }
            }
            public List<string> Inviteds
            {
                get { return inviteds; }
                set { inviteds = value; }
            }
            public int Size
            {
                get
                {
                    int size = Members.Count;
                    return size;
                }
            }

            public Domain()
            {
                members = new List<Member>();
                guests = new List<string>();
                inviteds = new List<string>();
                domains.Add("NOTAG", this);
            }
            public Domain(string tag, string name)
            {
                Tag = tag;
                Name = name;
                members = new List<Member>();
                guests = new List<string>();
                inviteds = new List<string>();
                domains.Add(tag, this);
            }
            public Domain(string tag, string name, string crownId)
            {
                Tag = tag;
                Name = name;
                members = new List<Member>();
                guests = new List<string>();
                inviteds = new List<string>();
                domains.Add(tag, this);
                AddMember(crownId);
                ChangeMemberRank(crownId, AssignRank("CROWN"));
                AssignPlayerNotes(crownId, "This player originally created the " + Name + " domain.");
            }

            public Member Crown
            {
                get
                {
                    foreach (var member in Members)
                    {
                        if (member.Rank == AssignRank("CROWN"))
                        {
                            return member;
                        }
                    }
                    return null;
                }
            }
            public bool IsMember (string playerId)
            {
                return (lookup[playerId] == this);
            }
            public bool IsGuest(string playerId)
            {
                return guests.Contains(playerId);
            }
            public bool IsInvited(string playerId)
            {
                return inviteds.Contains(playerId);
            }
            public Member FindMemberByID(string playerId)
            {
                if (IsMember(playerId))
                {
                    foreach (var member in Members)
                    {
                        if (playerId == member.UserID) return member;
                    }
                }
                return null;
            }
            public string GetMemberRank (string playerId)
            {
                var member = FindMemberByID(playerId);
                var rank = member.Rank;
                return rank;
            }
            public bool AddMember(string playerId)
            {
                var newMember = new Member() { UserID = playerId, Rank = AssignRank("CITIZEN"), PlayerNotes = "" };
                Members.Add(newMember);
                lookup.Add(playerId, this);
                if (!Members.Contains(newMember) || !(lookup[playerId] == this))
                {
                    return false;
                }
                if (Inviteds.Contains(playerId))
                {
                    Inviteds.Remove(playerId);
                }
                SortMemberListByRank();
                return true;
            }
            public bool RemoveMember(string playerId)
            {
                if (!IsMember(playerId))
                {
                    return false;
                }
                else
                {
                    var removedMember = FindMemberByID(playerId);
                    if (!(removedMember == null))
                    {
                        Members.Remove(removedMember);
                        lookup.Remove(playerId);
                        SortMemberListByRank();
                        return true;
                    }
                    else return false;
                }
                return false;
            }
            public bool ChangeMemberRank(string playerId, string newRank)
            {
                var changedMember = FindMemberByID(playerId);
                if (changedMember == null)
                {
                    return false;
                }
                else
                {
                    changedMember.Rank = AssignRank(newRank);
                    SortMemberListByRank();
                    return true;
                }
            }
            public bool AssignPlayerNotes(string playerId, string notes)
            {
                var member = FindMemberByID(playerId);
                if (member != null)
                {
                    member.PlayerNotes = notes;
                    return true;
                }
                return false;
            }
            public void Broadcast(string message) {
                string message_header = "<color=#a1ff46>(DOMAIN BROADCAST)</color> ";

                // Send message to members
                foreach (var member in members) { 
                    var player = BasePlayer.FindByID(Convert.ToUInt64(member.UserID));
                    if (player == null)
                        continue;
                    player.SendConsoleCommand("chat.add", "", message_header + message);
                }

            }
            private void SortMemberListByRank()
            {
                
                List<Member> newMemberList = new List<Member>();
                foreach (var rank in RankList)
                {
                    foreach (var member in Members)
                    {
                        if (member.Rank == rank) newMemberList.Add(member);
                    }
                }
                members = newMemberList;
            }
            public string DumpData()
            {
                var sb = new StringBuilder();
                sb.Append("[" + tag + "] " + name + " (members="+Members.Count+")\n");
                SortMemberListByRank();
                foreach (var member in Members)
                {
                    var memberName = FindPlayerNameByID(member.UserID);
                    sb.Append(member.UserID + "\t" + member.Rank + "\t" + memberName + "\t" + member.PlayerNotes);
                    sb.Append("\n");
                }
                foreach (var guest in Guests)
                {
                    var guestName = FindPlayerNameByID(guest);
                    sb.Append(guest + "\tGUEST\t" + guestName);
                    sb.Append("\n");
                }
                foreach (var invited in Inviteds)
                {
                    var invitedName = FindPlayerNameByID(invited);
                    sb.Append(invited + "\tINVITED\t" + invitedName);
                    sb.Append("\n");
                }
                return sb.ToString();
            }
        }

        protected override void LoadDefaultConfig()
        {
            Config.Clear();

            CheckCreateConfig();

            SaveConfig();
            Puts("Default config was saved and loaded!");
        }
        private void UpdateGUI()
        {
            DestroyGUI();
            AddGUI();
        }
        private void CheckCreateConfig()
        {
            if (Config["UpdateTimeInSeconds"] == null)
                Config["UpdateTimeInSeconds"] = 2;

            if (Config["ShowSeconds"] == null)
                Config["ShowSeconds"] = false;

            if (Config["BackgroundColor"] == null)
                Config["BackgroundColor"] = "0.1 0.1 0.1 0.3";

            if (Config["TextColor"] == null)
                Config["TextColor"] = "1 1 1 0.3";

            if (Config["FontSize"] == null)
                Config["FontSize"] = 14;

            if (Config["Position", "Left"] == null)
                Config["Position", "Left"] = 0.01;

            if (Config["Position", "Bottom"] == null)
                Config["Position", "Bottom"] = 0.95;

            if (Config["Size", "Width"] == null)
                Config["Size", "Width"] = 0.5;

            if (Config["Size", "Height"] == null)
                Config["Size", "Height"] = 0.03;

            if (Config["ServerTime"] == null)
                Config["ServerTime"] = false;

            if (Config["PreventChangingTime"] == null)
                Config["PreventChangingTime"] = false;

            if (Config["Messages", "Enabled"] == null)
                Config["Messages", "Enabled"] = "You have enabled clock";

            if (Config["Messages", "Disabled"] == null)
                Config["Messages", "Disabled"] = "You have disabled clock";

            if (Config["Messages", "STEnabled"] == null)
                Config["Messages", "STEnabled"] = "Now your clock shows server time";

            if (Config["Messages", "STDisabled"] == null)
                Config["Messages", "STDisabled"] = "Now your clock shows ingame time";

            if (Config["Messages", "Help"] == null)
                Config["Messages", "Help"] = "Clock:\n/clock - toggle clock\n/clock server - toggle server/ingame time";

            if (Config["Messages", "PreventChangeEnabled"] == null)
                Config["Messages", "PreventChangeEnabled"] = "You can't choose between server or ingame time";

            SaveConfig();
        }
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
