﻿// ScriptModel.cs
// Sharpen/Server
// This source code is subject to terms and conditions of the Apache License, Version 2.0.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Sharpen.Server.Configuration;
using Sharpen.Server.Core;

namespace Sharpen.Server.Script {

    internal sealed class ScriptModel {

        private string _scriptFlavor;
        private string _loaderScript;

        private ScriptInliner _scriptInliner;

        private List<Scriptlet> _scriptlets;
        private List<ScriptBlock> _scriptBlocks;
        private List<ScriptReference> _references;
        private Dictionary<string, ScriptReference> _referenceMap;

        public ScriptModel(string loaderScript, string scriptFlavor, ScriptInliner scriptInliner) {
            _loaderScript = loaderScript;
            _scriptFlavor = scriptFlavor;
            _scriptInliner = scriptInliner;

            _references = new List<ScriptReference>();
            _referenceMap = new Dictionary<string, ScriptReference>();
        }

        public string ScriptFlavor {
            get {
                return _scriptFlavor ?? String.Empty;
            }
        }

        public void AddScriptlet(Scriptlet scriptlet) {
            Debug.Assert(scriptlet != null);

            if (_scriptlets == null) {
                _scriptlets = new List<Scriptlet>();
            }

            _scriptlets.Add(scriptlet);
        }

        public void AddScriptBlock(ScriptBlock scriptBlock) {
            Debug.Assert(scriptBlock != null);

            if (_scriptBlocks == null) {
                _scriptBlocks = new List<ScriptBlock>();
            }

            _scriptBlocks.Add(scriptBlock);
        }

        public void AddScriptReference(ScriptReference scriptReference) {
            Debug.Assert(scriptReference != null);

            ScriptReference existingReference;
            if (_referenceMap.TryGetValue(scriptReference.Name, out existingReference)) {
                if ((int)existingReference.Mode > (int)scriptReference.Mode) {
                    existingReference.Mode = scriptReference.Mode;
                }
            }
            else {
                _references.Add(scriptReference);
                _referenceMap[scriptReference.Name] = scriptReference;
            }
        }

        public void IncludeDependencies(ScriptCollection configuredScripts) {
            HashSet<string> referenceSet = new HashSet<string>();
            Queue<string> dependencies = new Queue<string>();

            foreach (ScriptReference reference in _references) {
                referenceSet.Add(reference.Name);
                if (reference.Dependencies != null) {
                    foreach (string dependency in reference.Dependencies) {
                        dependencies.Enqueue(dependency);
                    }
                }
            }

            if (_scriptBlocks != null) {
                foreach (ScriptBlock scriptBlock in _scriptBlocks) {
                    if (scriptBlock.Dependencies != null) {
                        foreach (string dependency in scriptBlock.Dependencies) {
                            dependencies.Enqueue(dependency);
                        }
                    }
                }
            }

            while (dependencies.Count != 0) {
                string name = dependencies.Dequeue();
                if (referenceSet.Contains(name)) {
                    continue;
                }

                ScriptElement scriptElement = configuredScripts.GetElement(name, _scriptFlavor);
                string[] implicitDependencies = scriptElement.GetDependencyList();

                ScriptReference scriptReference =
                    new ScriptReference(name, scriptElement.Url, ScriptMode.OnDemand, implicitDependencies,
                                        scriptElement.Version);
                AddScriptReference(scriptReference);

                referenceSet.Add(name);

                if (implicitDependencies != null) {
                    foreach (string implicitDependency in implicitDependencies) {
                        if (referenceSet.Contains(implicitDependency) == false) {
                            dependencies.Enqueue(implicitDependency);
                        }
                    }
                }
            }
        }

        public void Render(TextWriter writer) {
            if (_references != null) {
                foreach (ScriptReference scriptReference in _references) {
                    if ((_scriptInliner != null) && _scriptInliner.CanInlineScript(scriptReference)) {
                        string scriptContent = _scriptInliner.GetScript(scriptReference);

                        if (scriptContent == null) {
                            RenderScriptTag(writer, /* path */ null, scriptReference.Name, scriptReference.Dependencies,
                                            scriptReference.Mode, "load", null);
                        }
                        else {
                            string version = scriptReference.Version.ToString(CultureInfo.InvariantCulture);
                            RenderScriptTag(writer, /* path */ null, scriptReference.Name, scriptReference.Dependencies,
                                            scriptReference.Mode, version, scriptContent);
                        }
                    }
                    else {
                        RenderScriptTag(writer, scriptReference.Path, scriptReference.Name, scriptReference.Dependencies,
                                        scriptReference.Mode, null, null);
                    }
                }
            }

            if (_scriptBlocks != null) {
                foreach (ScriptBlock scriptBlock in _scriptBlocks) {
                    RenderScriptTag(writer, /* path */ null, /* name */ null, scriptBlock.Dependencies,
                                    ScriptMode.Startup, /* storage */ null, scriptBlock.Code);
                }
            }

            // Write out the script loader reference.
            writer.Write("<script type=\"text/javascript\" src=\"");
            writer.Write(_loaderScript);
            writer.WriteLine("\"></script>");

            if (_scriptlets != null) {
                writer.WriteLine("<script type=\"text/javascript\">");
                writer.WriteLine("ss.ready(function() {");
                foreach (Scriptlet scriptlet in _scriptlets) {
                    writer.Write(scriptlet.ScriptletType);
                    writer.Write("Scriptlet(");
                    if (scriptlet.Parameters != null) {
                        JsonWriter jsonWriter = new JsonWriter(writer, /* minimizeWhitespace */ true);
                        jsonWriter.WriteValue(scriptlet.Parameters);
                    }
                    writer.WriteLine(");");
                }
                writer.WriteLine("});");
                writer.WriteLine("</script>");
            }
        }

        private void RenderScriptTag(TextWriter writer, string path, string name, string[] dependencies, ScriptMode mode, string storage, string script) {
            writer.Write("<script type=\"text/script\"");

            if (String.IsNullOrEmpty(path) == false) {
                writer.Write(" data-src=\"");
                writer.Write(path);
                writer.Write("\"");
            }

            if (String.IsNullOrEmpty(name) == false) {
                writer.Write(" data-name=\"");
                writer.Write(name);
                writer.Write("\"");
            }

            if (dependencies != null) {
                writer.Write(" data-requires=\"");
                for (int i = 0; i < dependencies.Length; i++) {
                    writer.Write(dependencies[i]);
                    if (i != dependencies.Length - 1) {
                        writer.Write(",");
                    }
                }
                writer.Write("\"");
            }

            if (storage != null) {
                writer.Write(" data-store=\"");
                writer.Write(storage);
                writer.Write("\"");
            }

            if (mode != ScriptMode.Startup) {
                writer.Write(" data-mode=\"");
                if (mode == ScriptMode.Deferred) {
                    writer.Write("deferred");
                }
                else {
                    Debug.Assert(mode == ScriptMode.OnDemand);
                    writer.Write("ondemand");
                }
                writer.Write("\"");
            }

            if (String.IsNullOrEmpty(script)) {
                writer.WriteLine("></script>");
            }
            else {
                writer.WriteLine(">");
                writer.Write(script);
                writer.WriteLine("</script>");
            }
        }
    }
}
