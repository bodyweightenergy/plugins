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

        private string GovernmentDataFilename = "GovernmentData";
        private Dictionary<string, Government> govs;
        private Dictionary<string, Government> lookup;
        private Dictionary<string, string> originalNames;
        private Dictionary<string, List<string>> permissionList;
        private List<string> RankList;

        #endregion //Plugin Private Members

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
