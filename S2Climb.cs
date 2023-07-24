using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Teal;
using MiniJSON;

public class S2Climb : MonoBehaviour
{
    const string basePath = "S2Climb/";
    const string playerPath = basePath+"playerdata/";
    const string mapPath = basePath+"mapdata/";

    const string finishMarker = "SM_Prop_Sign_Stop_01";
    const float finishRange = 1.5f;
    const float minFinishTime = 1.0f;

    const Packets hotkey_packet = Packets.Custom+1;

    const float saveload_speed = 0.5f;

    List<string> modifiers = new List<string>{
        "ctrl",
        "alt",
        "cmd"
    };

    public class PlayerData {
        public class Hotkey {
            public KeyCode key;
            public List<string> mods;

            public Hotkey() {
                key = KeyCode.None;
                mods = new List<string>();
            }

            public Hotkey(string k, params string[] mlist) {
                key = (KeyCode)Enum.Parse(typeof(KeyCode),k,true);
                mods = new List<string>(mlist);
            }

            public Dictionary<string,object> to_dict() {
                return new Dictionary<string,object>() {
                    {"key",key.ToString()},
                    {"mods",mods}
                };
            }

            public void from_dict(Dictionary<string,object> dict) {
                key = (KeyCode)Enum.Parse(typeof(KeyCode),(string)dict["key"]);
                mods.Clear();
                foreach( object o in (List<object>)dict["mods"] ) {
                    mods.Add( (string)o );
                }
            }

            public bool is_pressed() {
                if( Input.GetKeyDown(key) ) {
                    foreach( string mod in mods )
                        if( !Input.GetKey("left "+mod) &&
                            !Input.GetKey("right "+mod)
                        ) return false;
                    return true;
                }
                return false;
            }
        } //> end class Hotkey

        public string pfid;
        public string last_username;
        public Dictionary<string,Hotkey> commands;
        public Vector2 save_pos;
        public bool save_active;
        public float start_time;
        public float finish_time;

        public PlayerData() {
            Clear();
        }

        public void Clear() {
            pfid = "";
            last_username = "";
            commands = new Dictionary<string,Hotkey>() {
                {"save", new Hotkey("c","ctrl")},
                {"load", new Hotkey("v","ctrl")},
                {"reset", new Hotkey("r","ctrl")}
            };
            save_pos = new Vector2();
            save_active = false;
            start_time = RealTime.timeSinceLevelLoad;
            finish_time = -1f;
        }

        public void SaveData() {
            if( pfid == "" ) return;
            var data = new Dictionary<string,object>() {
                {"last_username", last_username}
            };
            foreach( var entry in commands )
                data.Add(entry.Key, entry.Value.to_dict());
            FileHelper.WriteJson(playerPath+pfid+".json",data);
        }

        public void LoadData() {
            if( pfid == "" ) return;
            if( FileHelper.Exists(playerPath+pfid+".json") ) {
                var data = FileHelper.ReadJson(playerPath+pfid+".json");
                last_username = (string)data["last_username"];
                foreach( var entry in commands )
                    entry.Value.from_dict(
                        (Dictionary<string,object>)(data[entry.Key])
                    );
            } else {
                Debug.Log("player not found: "+pfid);
                Clear();
            }
        }
    } //> end class PlayerData

    public class ClimbScore: IComparable<ClimbScore> {
        public string pid;
        public float time;
        public string date;

        public ClimbScore() {
            pid = "";
            time = -1f;
            date = "";
        }

        public ClimbScore(string p, float t) {
            pid = p;
            time = t;
            date = System.DateTime.Now.ToString("dd/MM/yyyy");
        }

        public ClimbScore(Dictionary<string,object> dict) {
            from_dict(dict);
        }

        public Dictionary<string,object> to_dict() {
            return new Dictionary<string,object>() {
                {"pid",pid},
                {"time",time},
                {"date",date}
            };
        }

        public void from_dict(Dictionary<string,object> dict) {
            pid = (string)dict["pid"];
            time = (float)((double)dict["time"]);
            date = (string)dict["date"];
        }

        public int CompareTo(ClimbScore other) {
            if(this.time > other.time) return 1;
            if(this.time < other.time) return -1;
            return 0;
        }
    } //> end class ClimbScore

    public class MapScores {
        public string mapname;
        List<ClimbScore> scores;
        bool sorted;

        public MapScores() {
            mapname = "";
            scores = new List<ClimbScore>();
            sorted = true;
        }

        public MapScores(string mname) {
            mapname = mname;
            scores = new List<ClimbScore>();
            LoadData();
        }

        public ClimbScore this[int idx] {
            get {
                if(!sorted) {
                    scores.Sort();
                    sorted = true;
                }
                return scores[idx];
            }
            set {
                scores[idx] = value;
            }
        }

        public int Count() {
            return scores.Count();
        }

        public List<ClimbScore> FindAll(Predicate<ClimbScore> match) {
            if(!sorted) {
                scores.Sort();
                sorted = true;
            }
            return scores.FindAll(match);
        }

        public void Add(ClimbScore score) {
            scores.Add(score);
            sorted = false;
        }

        public void SaveData() {
            if( mapname == "" ) return;
            var scorelist = new List<Dictionary<string,object>>();
            if(!sorted) {
                scores.Sort();
                sorted = true;
            }
            foreach( ClimbScore cs in scores )
                scorelist.Add(cs.to_dict());
            var data = new Dictionary<string,object>() {
                {"scores",scorelist}
            };
            FileHelper.WriteJson(mapPath+mapname+".json",data);
        }

        public void LoadData() {
            scores.Clear();
            sorted = true;
            if( mapname == "" ) return;
            if( FileHelper.Exists(mapPath+mapname+".json") ) {
                var data = FileHelper.ReadJson(mapPath+mapname+".json");
                var scorelist = data["scores"] as List<object>;
                foreach( object d in scorelist ) {
                    var dict = d as Dictionary<string,object>;
                    scores.Add(new ClimbScore(dict));
                }
            } else {
                Debug.Log("map not found: "+mapname);
            }
        }
    } //> end class MapScores

    Respawning respawning;
    StandardRespawning standardRespawning;
    Match match;

    Vector2? finishPoint = null;
    MapScores mdata = new MapScores();
    Dictionary<int,PlayerData> pdata = new Dictionary<int,PlayerData>();
    Dictionary<string,string> pnames = new Dictionary<string,string>();
    CommandCore.CommandSystem cs;

    void FindFinishPoint()
    {
        int count = 0;
        foreach( PropModel pm in GameObject.FindObjectsOfType<PropModel>() ) {
            if( pm.mapData.model == finishMarker ) {
                finishPoint = pm.transform.position;
                count++;
            }
        }

        if( count==0 ) {
            finishPoint = null;
            GameChat.ChatOrLog("S2Climb: ERROR finish marker not found - timing and scores not available!");
        } else if( count>1 ) {
            GameChat.ChatOrLog("S2Climb: WARNING multiple finish markers present, using position of the last marker!");
        }
    }

    void EndRun(int pid)
    {
        float time = RealTime.timeSinceLevelLoad - pdata[pid].start_time;
        if( time < minFinishTime ) return;

        Player p = Players.Get.GetPlayerByID((ushort)pid);
        pdata[pid].finish_time = RealTime.timeSinceLevelLoad;
        mdata.Add( new ClimbScore( (string)p.props["account"], time ) );

        GameChat.ChatOrLog( String.Format(
            "{0} finished with a time of {1:00}:{2:00}:{3:000}!",
            p.nick,
            Mathf.FloorToInt(time / 60f),
            Mathf.FloorToInt(time % 60f),
            (time-Mathf.Floor(time)) * 1000f
        ));
    }

    void Respawn(Player p) {
        pdata[p.id].save_active = false;
        pdata[p.id].finish_time = -1.0f;
        if( respawning.IsPlayerInQueue(p.id) ) {
            Respawning.RespawnObject ro = respawning.GetPlayerQueue(p.id);
            pdata[p.id].start_time = RealTime.timeSinceLevelLoad+ro.waitSecs;
        } else {
            RespawningCommon.SpawnPlayer(p, 0f);
            pdata[p.id].start_time = RealTime.timeSinceLevelLoad;
        }
    }

    string TopScores(int pid, IEnumerable<string> args)
    {
        int nargs = args.Count();
        int ntop = 5;
        MapScores ms;

        if( nargs > 2 ) return "Too many arguments";
        if( nargs > 0 && !Int32.TryParse(args.ElementAt(0), out ntop) ) return "Invalid argument";
        if( ntop < 1 ) return "Invalid argument";
        if( nargs == 2 ) {
            string map = args.ElementAt(1);
            if( !FileHelper.Exists(mapPath+map+".json") ) return "Map not found";
            ms = new MapScores(map);
        } else {
            ms = mdata;
        }

        int nscores = Math.Min(ntop,ms.Count());
        for( int i=0; i<nscores; i++ ) {
            ClimbScore s = ms[i];
            string ss = String.Format("{0}: {1}s - {2} {3}", i+1, s.time, LookupPlayer(s.pid), s.date);
            GameChat.instance.ServerChat(ss,(ushort)pid);
        }
        return "";
    }

    string SetHotkey(int pid, IEnumerable<string> args)
    {
        int nargs = args.Count();
        PlayerData.Hotkey hk;
        KeyCode kc;

        if( nargs<2 ) return "Insufficient arguments";
        if( !pdata[pid].commands.TryGetValue(args.ElementAt(0), out hk) ) return "Invalid action";

        hk.mods.Clear();
        for( int i=1; i<nargs-1; i++ ) {
            if( modifiers.Contains(args.ElementAt(i).ToLower()) )
                hk.mods.Add(args.ElementAt(i).ToLower());
        }
        string k = args.ElementAt(nargs-1).ToString();
        if( !Enum.TryParse(k,true,out kc) )
            return "Invalid key";
        hk.key = kc;
        return "";
    }

    string LookupPlayer(string pid)
    {
        string name;
        if ( pnames.TryGetValue(pid, out name) ) return name;
        else {
            foreach( Player p in Players.Get.GetHumans() ) {
                if( (string)p.props["account"] == pid ) {
                    pnames.Add(pid,p.nick);
                    return p.nick;
                }
            }
            name = (string)FileHelper.ReadJson(playerPath+pid+".json")["last_username"];
            pnames.Add(pid,name);
            return name;
        }
    }

    void SendHotkeyEvent(string eventname)
    {
        Pump.temp.Clear();
        Pump.temp.WriteStringASCII(eventname);
        GameClient.Get.Send(hotkey_packet, Pump.temp.Pack(), SendFlags.Reliable);
    }


    void Master_RespawnImmediatelyAtPosition(Player p, Vector2 position)
    {
        System.Func<string, Player, int, Vector2> respawnLocation = (prefab, player, team) => position;
        respawning.DoRespawn("Gostek", p.GetTeam(), p.id, 0.0f, respawnLocation);
    }

    private void Awake()
    {
        Eventor.AddListener(Events.Match_Started, OnMatchStarted);
        Eventor.AddListener(Events.Match_Ended, OnMatchEnded);
        Eventor.AddListener(Events.Player_Joined, OnPlayerJoined);
        Eventor.AddListener(Events.Player_Left, OnPlayerLeft);
        Eventor.AddListener(Events.Died, OnDied);
        GameChat.instance.OnChat.AddListener(OnPlayerChat);
        GameServer.Get.Messaged.AddListener(OnMessageFromClient);
    }

    private void OnDestroy()
    {
        mdata.SaveData();
        foreach( var entry in pdata ) {
            entry.Value.SaveData();
        }

        Eventor.RemoveListener(Events.Match_Started, OnMatchStarted);
        Eventor.RemoveListener(Events.Match_Ended, OnMatchEnded);
        Eventor.RemoveListener(Events.Player_Joined, OnPlayerJoined);
        Eventor.RemoveListener(Events.Player_Left, OnPlayerLeft);
        Eventor.RemoveListener(Events.Died, OnDied);
        GameChat.instance.OnChat.RemoveListener(OnPlayerChat);
        GameServer.Get.Messaged.RemoveListener(OnMessageFromClient);
    }

    void Start()
    {
        respawning = GetComponent<Respawning>();
        standardRespawning = GetComponent<StandardRespawning>();
        standardRespawning.allowRespawning = false;
        match = GetComponent<Match>();

        cs = new CommandCore.CommandSystem("!", new List<CommandCore.Command>{
            new CommandCore.Command("top", "top [nscores] [map]", TopScores),
            new CommandCore.Command("bind", "bind {save|load|reset} [ctrl|alt|cmd ...] {key}", SetHotkey)
        });

        foreach( Player p in Players.Get.GetHumans() ) {
            if( p ) {
                pdata.TryAdd( (int)p.id, new PlayerData() );
                pdata[p.id].pfid = (string)p.props["account"];
                pdata[p.id].last_username = p.nick;
                pdata[p.id].LoadData();
            }
        }
    }

    void OnMatchStarted(IGameEvent e)
    {
        mdata.mapname = Map.GetLevelName();
        mdata.LoadData();

        foreach( Player p in Players.Get.GetHumans() ) {
            if( p ) {
                pdata[p.id].start_time = RealTime.timeSinceLevelLoad;
                pdata[p.id].finish_time = -1.0f;
            }
        }

        FindFinishPoint();
    }

    void OnMatchEnded(IGameEvent e)
    {
        mdata.SaveData();

        foreach( Player p in Players.Get.GetHumans() ) {
            if( p ) {
                pdata[p.id].save_active = false;
            }
        }
    }

    void OnPlayerJoined(IGameEvent e)
    {
        GlobalPlayerEvent ev = e as GlobalPlayerEvent;
        if( !Players.Get.justConnected && !ev.Player.IsBot() ) {
            pdata.TryAdd( (int)ev.Player.id, new PlayerData() );
            pdata[ev.Player.id].pfid = (string)ev.Player.props["account"];
            pdata[ev.Player.id].last_username = ev.Player.nick;
            pdata[ev.Player.id].LoadData();
        }
    }

    void OnPlayerLeft(IGameEvent e)
    {
        GlobalPlayerEvent ev = e as GlobalPlayerEvent;
        if( !ev.Player.IsBot() ) {
            pdata[ev.Player.id].pfid = (string)ev.Player.props["account"];
            pdata[ev.Player.id].SaveData();
        }
    }

    void OnDied(IGameEvent e)
    {
        Respawn(e.Sender.GetComponent<Controls>().player);
    }

    void OnPlayerChat(Player p, string msg)
    {
        if(!p) return;
        string er = cs.Parse(p.id,msg);
        if( er == "" ) return;
        Debug.Log(er);
        foreach( string line in er.Split('\n') )
            GameChat.instance.ServerChat(line,p.id);
    }

    void OnMessageFromClient(Packets eventCode, byte[] data, ushort senderID)
    {
        if(!gameObject.activeInHierarchy) return;
        if(eventCode == hotkey_packet) {
            Pump.temp.Unpack(data,data.Length);
            string msg = Pump.temp.ReadStringASCII();
            // Debug.Log("Received "+msg+" from "+senderID);

            Player p = Players.Get.GetPlayerByID(senderID);

            if( msg == "save" ) {
                if( p.controlled.GetComponent<GostekMovement>().v.velMag < saveload_speed ) {
                    pdata[senderID].save_pos = p.controlled.transform.position;
                    pdata[senderID].save_active = true;
                    GameChat.instance.ServerChat("Position saved",senderID);
                } else {
                    GameChat.instance.ServerChat("Moving too fast to save!",senderID);
                }
            }

            if( msg == "load" ) {
                if( pdata[senderID].save_active ) {
                    if( p.controlled.GetComponent<GostekMovement>().v.velMag < saveload_speed )
                        Master_RespawnImmediatelyAtPosition(p, pdata[senderID].save_pos);
                    else
                        GameChat.instance.ServerChat("Moving too fast to load!",senderID);
                } else {
                    GameChat.instance.ServerChat("No valid save point",senderID);
                }
            }

            if( msg == "reset" ) {
                pdata[senderID].finish_time = -1.0f;
                Respawn(p);
            }
        }
    }

    void OnGUI()
    {
        Controls local = Players.Get.GetLocalPlayerControlled();
        if( local && finishPoint != null ) {
            int pid = local.GetPlayerId();

            GUI.skin.label.fontSize = 48;
            GUI.skin.label.normal.textColor = new Color(1f, 1f, 1f, 0.7f);

            // Show run time
            float elapsed;
            if( pdata[pid].finish_time<0 )
                elapsed = RealTime.timeSinceLevelLoad - pdata[pid].start_time;
            else
                elapsed = pdata[pid].finish_time - pdata[pid].start_time;

            // Format the time as minutes:seconds:milliseconds
            int minutes = Mathf.FloorToInt(elapsed / 60f);
            int seconds = Mathf.FloorToInt(elapsed % 60f);
            int milliseconds = Mathf.FloorToInt((elapsed - Mathf.Floor(elapsed)) * 1000f);
            string timeText = string.Format("{0:00}:{1:00}:{2:000}", minutes, seconds, milliseconds);

            // Display the formatted time
            GUI.Label(new Rect((Screen.width - 200) / 2, Screen.height - Screen.height / 4, 300, 130), "\n" + timeText);
        }
    }

    void Update()
    {
        foreach (Player player in Players.Get.GetHumans()) {
            if( player ) {
                PlayerData pd;
                if( pdata.TryGetValue(player.id, out pd) ) {
                    foreach( var entry in pd.commands )
                        if( entry.Value.is_pressed() )
                            SendHotkeyEvent(entry.Key);
                            //TODO: don't trigger if chat is open
                }
            }
        }

        if( match.state == MatchState.InProgress )
            foreach( Player player in Players.Get.GetAliveHumans() )
                if( finishPoint != null &&
                    pdata[player.id].finish_time<0 &&
                    ((Vector2)player.controlled.transform.position - finishPoint.Value).magnitude < finishRange ) {
                        EndRun(player.id);
                    }
    }
}
