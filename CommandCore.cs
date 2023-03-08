namespace CommandCore
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    public delegate string CommandFunc(int pid, IEnumerable<string> args);

    public class Command {
        public string name;
        public string helptext;
        public CommandFunc action;

        public Command(string n, CommandFunc a) {
            name = n;
            helptext = "";
            action = a;
        }
        public Command(string n, string h, CommandFunc a) {
            name = n;
            helptext = h;
            action = a;
        }
    }

    public class CommandSystem {
        public string prefix;
        Dictionary<string,Command> commands;

        public CommandSystem(string pre, Dictionary<string,Command> cmds) {
            prefix = pre;
            commands = cmds;
        }

        public CommandSystem(string pre, List<Command> cmds) {
            prefix = pre;
            commands = new Dictionary<string,Command>();
            foreach( Command c in cmds ) {
                commands.Add(c.name,c);
            }
        }

        public string Parse(int pid, string msg) {
            if( !msg.StartsWith(prefix) ) return "";
            var subs = msg.Substring(prefix.Length).Split(' ');
            Command cmd;
            string error = "";
            if( commands.TryGetValue(subs[0], out cmd) ) {
                error = cmd.action(pid, subs.Skip(1));
                if( error != "" )
                    error += "\n"+cmd.helptext;
            }
            return error;
        }
    }
}
