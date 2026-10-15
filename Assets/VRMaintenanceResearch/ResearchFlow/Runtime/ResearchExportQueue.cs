using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Immutable export snapshot. The calling (main) thread validates identity and freezes each append-only file length;
    /// one background worker hashes, copies and queues in request order. PC receipt/QC, not this queue, proves delivery.</summary>
    public static class ResearchExportQueue
    {
        static readonly string[] Files={"events.jsonl","movement.csv","session_metadata.json","research-session.json"};
        static readonly UTF8Encoding Utf8=new UTF8Encoding(false);
        static readonly object WorkerGate=new object();
        static Task worker=Task.CompletedTask;
        static string Hash(byte[] bytes){using(var sha=SHA256.Create())return string.Concat(sha.ComputeHash(bytes).Select(b=>b.ToString("x2",CultureInfo.InvariantCulture)));}
        static string HashFile(string path){using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))return Prefix(file,file.Length,null);}
        static byte[] Json(object value)=>Utf8.GetBytes(JsonConvert.SerializeObject(value,Formatting.Indented));
        static void Token(string value)
        {
            if(string.IsNullOrWhiteSpace(value)||value.Length>160||!value.All(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'))throw new InvalidDataException("Unsafe export identity");
        }
        sealed class Request
        {
            public string DataRoot,SourceDirectory,Reason,Lifecycle,CreatedUtc,Participant,Session,Run,Group;
            public bool TestData,Saved;
            public byte[] RecordBytes;
            public JObject Provenance;
            public long[] Lengths;
            public Action VerifyRecord;
        }
        public static Task<string> Snapshot(string dataRoot,HandsLabLogger logger,ParticipantRecord record,string reason,string lifecycle,JObject provenance=null)
        {
            if(logger==null||record==null)throw new ArgumentNullException("Active logger and participant record required");
            Token(record.participant_id);Token(record.session_id);Token(record.run_id);
            if(record.run_id!=logger.RunId)throw new InvalidDataException("Export run identity mismatch");
            if(lifecycle!="finalized"&&lifecycle!="task_finalized"&&lifecycle!="partial")throw new ArgumentException("Explicit export lifecycle required");
            if(lifecycle=="finalized"&&!logger.DataComplete)throw new InvalidOperationException("Logger has not finalized; use partial snapshot");
            if(ResearchParticipantRegistry.IsTechnical(record.group)!=record.test_data)throw new InvalidDataException("Export group/provenance mismatch");
            // The logger writes whole flushed rows on this thread and only appends, so these prefixes are the session as of this call.
            return Enqueue(Freeze(dataRoot,logger.DirectoryPath,record,reason,lifecycle,provenance,false,null));
        }
        public static Task<string> SnapshotSavedRun(ResearchParticipantRegistry registry,string participant,string session,string run,string activeRun=null,string reason="operator_export_saved")
        {
            if(registry==null)throw new ArgumentNullException(nameof(registry));
            Token(participant);Token(session);Token(run);
            if(run==activeRun)throw new InvalidOperationException("Current run cannot use saved-run export");
            Func<ParticipantRecord> resolve=()=>new[]{ParticipantGroup.Dev,ParticipantGroup.Test,ParticipantGroup.Pilot}
                .SelectMany(registry.List).SingleOrDefault(r=>r.participant_id==participant&&r.session_id==session&&r.run_id==run);
            var record=resolve();
            if(record==null||!record.test_data)throw new InvalidDataException("Exact saved technical participant/session/run required");
            byte[] savedRecord=Json(record);
            // The registry is file-locked and replaced atomically, so the worker may re-read it.
            Action verifyRecord=()=>{var fresh=resolve();if(fresh==null||!Json(fresh).SequenceEqual(savedRecord))throw new IOException("Saved registry record changed during snapshot");};
            var provenance=JObject.FromObject(new{device="unknown",render="unknown",simulated_presence="unknown",apk_sha256=(string)null,source_code_version=(string)null,hardware_verified=false,
                saved_run=true,historical_build_provenance="unknown_pre_upgrade",validity="preserved_raw_pending_host_QC"});
            return Enqueue(Freeze(registry.DirectoryPath,Path.Combine(registry.DirectoryPath,"Logs",run),record,reason,"partial",provenance,true,verifyRecord));
        }
        static Request Freeze(string dataRoot,string sourceDirectory,ParticipantRecord record,string reason,string lifecycle,JObject provenance,bool saved,Action verifyRecord)
        {
            // Cheap preflight so an obstructed destination is refused to the operator now, not after the reply.
            Directory.CreateDirectory(Path.Combine(dataRoot,"Exports"));Directory.CreateDirectory(Path.Combine(dataRoot,"ExportQueue"));
            return new Request{DataRoot=dataRoot,SourceDirectory=sourceDirectory,Reason=reason,Lifecycle=lifecycle,CreatedUtc=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture),
                Participant=record.participant_id,Session=record.session_id,Run=record.run_id,Group=record.group.ToString(),TestData=record.test_data,Saved=saved,
                RecordBytes=Json(record),Provenance=(JObject)provenance?.DeepClone(),VerifyRecord=verifyRecord,
                Lengths=Files.Take(3).Select(file=>new FileInfo(Path.Combine(sourceDirectory,file)).Length).ToArray()};
        }
        static Task<string> Enqueue(Request request)
        {
            lock(WorkerGate)
            {
                var next=worker.ContinueWith(_=>SnapshotFiles(request),CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default);
                worker=next;
                return next;
            }
        }
        public static string DeliveryStatus(string dataRoot,ParticipantRecord record,string lastExportPath=null)
        {
            if(record==null||string.IsNullOrEmpty(record.run_id))return "saved";
            try
            {
                Token(record.participant_id);Token(record.session_id);Token(record.run_id);
                string queue=Path.Combine(dataRoot,"ExportQueue");
                JObject latest=null;DateTimeOffset latestTime=DateTimeOffset.MinValue;long latestRevision=-1;bool ambiguous=false;
                if(Directory.Exists(queue))foreach(string path in Directory.EnumerateFiles(queue,"*.json"))
                {
                    JObject job;
                    try{job=ReadObject(path);}catch(JsonException){continue;}
                    if(!Identity(job,record))continue;
                    if((string)job["schema_version"]!="researchflow.export_queue.1"||
                        !Utc(job["created_utc"],out var created))return "attention";
                    string id=Text(job["bundle_id"]);Token(id);
                    if(Path.GetFileName(path)!=id+".json")return "attention";
                    var savedRecord=ReadObject(Path.Combine(dataRoot,"Exports",id,"raw","research-session.json"));
                    if(savedRecord["revision"]?.Type!=JTokenType.Integer||!long.TryParse(savedRecord["revision"].ToString(),out var revision)||revision<0)return "attention";
                    if(latest==null||created>latestTime||(created==latestTime&&revision>latestRevision))
                    {latest=job;latestTime=created;latestRevision=revision;ambiguous=false;}
                    else if(created==latestTime&&revision==latestRevision&&id!=Text(latest["bundle_id"]))ambiguous=true;
                }
                if(latest==null)return string.IsNullOrEmpty(lastExportPath)?"saved":"attention";
                if(ambiguous)return "attention";
                string bundle=Text(latest["bundle_id"]);
                string directory=Path.Combine(dataRoot,"Exports",bundle);
                if(!string.Equals(Path.GetFullPath(Text(latest["export_path"])),Path.GetFullPath(directory),StringComparison.OrdinalIgnoreCase))return "attention";
                byte[] manifestBytes=File.ReadAllBytes(Path.Combine(directory,"manifest.json"));
                string manifestHash=Hash(manifestBytes);
                var manifest=ReadObject(manifestBytes);
                var complete=ReadObject(Path.Combine(directory,"COMPLETE.json"));
                if((string)manifest["schema_version"]!="researchflow.device-bundle.1"||
                    (string)manifest["bundle_id"]!=bundle||!Identity(manifest["identity"] as JObject,record)||
                    (string)complete["schema_version"]!="researchflow.bundle-complete.1"||
                    (string)complete["bundle_id"]!=bundle||(string)complete["manifest_sha256"]!=manifestHash)return "attention";
                var recordHash=(string)manifest["files"]?["raw/research-session.json"]?["sha256"];
                byte[] exportedBytes=File.ReadAllBytes(Path.Combine(directory,"raw","research-session.json"));
                if(!Sha(manifest["files"]?["raw/research-session.json"]?["sha256"])||recordHash!=Hash(exportedBytes))return "attention";
                var exported=ReadObject(exportedBytes);
                if(Text(exported["participant_id"])!=record.participant_id||Text(exported["session_id"])!=record.session_id||Text(exported["run_id"])!=record.run_id)return "attention";
                if(record.session_state==RegistryState.Aborted||record.session_state==RegistryState.Invalid||record.session_state==RegistryState.Completed)
                {
                    var current=JObject.FromObject(record);
                    foreach(string field in new[]{"session_state","termination_reason","termination_amendments"})
                        if(!JToken.DeepEquals(exported[field],current[field]))return "attention";
                }
                string receiptPath=Path.Combine(dataRoot,"ExportReceipts",bundle+".json");
                if(!File.Exists(receiptPath))return "queued";
                var receipt=ReadObject(receiptPath);
                if((string)receipt["schema_version"]!="researchflow.pc-receipt.1"||
                    (string)receipt["bundle_id"]!=bundle||!Identity(receipt,record)||
                    (string)receipt["source_manifest_sha256"]!=manifestHash||
                    !Sha(receipt["pc_manifest_sha256"])||!Sha(receipt["receipt_sha256"])||
                    receipt["qc_data_complete"]?.Type!=JTokenType.Boolean||
                    receipt["qc_failure_count"]?.Type!=JTokenType.Integer||
                    !long.TryParse(receipt["qc_failure_count"].ToString(),NumberStyles.None,CultureInfo.InvariantCulture,out var failures)||
                    !Utc(receipt["received_utc"],out var received))return "attention";
                Token(Text(receipt["pc_bundle_id"]));
                return failures==0?"received":"attention";
            }
            catch(Exception ex) when(ex is InvalidDataException||ex is IOException||ex is UnauthorizedAccessException||ex is JsonException||ex is ArgumentException||ex is InvalidOperationException||ex is FormatException||ex is OverflowException){return "attention";}
        }
        static JObject ReadObject(string path)=>ReadObject(File.ReadAllBytes(path));
        static JObject ReadObject(byte[] bytes)
        {
            using(var reader=new JsonTextReader(new StringReader(Utf8.GetString(bytes))){DateParseHandling=DateParseHandling.None})
            {
                var value=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
                if(reader.Read())throw new JsonReaderException("Trailing JSON content");
                return value;
            }
        }
        static string Text(JToken value)=>value?.Type==JTokenType.String?(string)value:null;
        static bool Identity(JObject value,ParticipantRecord record)=>value!=null&&
            Text(value["participant_code"])==record.participant_id&&Text(value["session_id"])==record.session_id&&Text(value["run_id"])==record.run_id;
        static bool Sha(JToken value)
        {
            string text=Text(value);return text!=null&&text.Length==64&&text.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');
        }
        static bool Utc(JToken value,out DateTimeOffset parsed)
        {
            string text=Text(value);
            return DateTimeOffset.TryParseExact(text,new[]{"yyyy-MM-dd'T'HH:mm:ss'Z'","yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'","yyyy-MM-dd'T'HH:mm:sszzz","yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"},CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out parsed)&&parsed.Offset==TimeSpan.Zero;
        }

        static string SnapshotFiles(Request request)
        {
            string sourceDirectory=request.SourceDirectory;bool saved=request.Saved;
            var sources=new Dictionary<string,FileStream>();
            try
            {
            foreach(string file in Files.Take(3))sources[file]=new FileStream(Path.Combine(sourceDirectory,file),FileMode.Open,FileAccess.Read,saved?FileShare.Read:FileShare.ReadWrite);
            var lengths=new Dictionary<string,long>();
            for(int i=0;i<3;i++)lengths[Files[i]]=request.Lengths[i];
            if(saved)
            {
                var bytes=new MemoryStream();Prefix(sources[Files[2]],lengths[Files[2]],bytes);
                var metadata=JObject.Parse(Utf8.GetString(bytes.ToArray()));
                if((string)metadata["run_id"]!=request.Run||(bool?)metadata["technical_test"]!=true||(bool?)metadata["test_data"]!=true)throw new InvalidDataException("Saved metadata identity/provenance mismatch");
            }
            var fileHashes=new Dictionary<string,string>();
            var modified=new Dictionary<string,DateTime>();
            foreach(string file in Files.Take(3))
            {
                string path=Path.Combine(sourceDirectory,file);
                modified[file]=File.GetLastWriteTimeUtc(path);
                // A live writer may append past the frozen prefix; a saved run must not change at all.
                if(saved?sources[file].Length!=lengths[file]:sources[file].Length<lengths[file])throw new IOException("Snapshot changed while reading: "+file);
                fileHashes[file]=Prefix(sources[file],lengths[file],null);
                if(saved&&(sources[file].Length!=lengths[file]||File.GetLastWriteTimeUtc(path)!=modified[file]))throw new IOException("Snapshot changed while reading: "+file);
            }
            request.VerifyRecord?.Invoke();
            byte[] recordBytes=request.RecordBytes;fileHashes[Files[3]]=Hash(recordBytes);lengths[Files[3]]=recordBytes.LongLength;
            var hashes=fileHashes.ToDictionary(pair=>"raw/"+pair.Key,pair=>(object)new{bytes=lengths[pair.Key],sha256=pair.Value});
            var identity=new{participant_code=request.Participant,session_id=request.Session,run_id=request.Run,group=request.Group,test_data=request.TestData};
            string lifecycle=request.Lifecycle,reason=request.Reason;
            var flags=request.Provenance??JObject.FromObject(new{device="unknown",render="unknown",simulated_presence="unknown",apk_sha256=(string)null,source_code_version=(string)null,hardware_verified=false});
            string bundleId="snapshot_"+Hash(Json(new{identity,lifecycle,reason,provenance=flags,files=hashes})).Substring(0,24);
            string exports=Path.Combine(request.DataRoot,"Exports"),queue=Path.Combine(request.DataRoot,"ExportQueue");
            Directory.CreateDirectory(exports);Directory.CreateDirectory(queue);
            string final=Path.Combine(exports,bundleId);
            var manifest=new{schema_version="researchflow.device-bundle.1",bundle_id=bundleId,identity,lifecycle,data_completeness="pending_host_QC",delivery_status="queued_for_host",reason,provenance=flags,files=hashes,clock="logger monotonic seconds; native sensor timestamps unavailable",snapshot_policy=saved?"read-only saved run; exclusive read leases and source hashes rechecked; raw bytes preserved":"append-only writer prefixes frozen on the main thread, then copied and hashed on a background worker; raw bytes preserved"};
            byte[] manifestBytes=Json(manifest);
            if(!Directory.Exists(final))
            {
                string stage=Path.Combine(exports,".staging_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);Directory.CreateDirectory(Path.Combine(stage,"raw"));
                if(saved)CheckSources(sources,sourceDirectory,fileHashes,lengths,modified);request.VerifyRecord?.Invoke();
                foreach(string file in Files.Take(3))
                    using(var output=new FileStream(Path.Combine(stage,"raw",file),FileMode.CreateNew,FileAccess.Write,FileShare.Read))
                        if(Prefix(sources[file],lengths[file],output)!=fileHashes[file])throw new IOException("Source prefix changed during snapshot: "+file);
                File.WriteAllBytes(Path.Combine(stage,"raw",Files[3]),recordBytes);
                foreach(var pair in fileHashes)if(HashFile(Path.Combine(stage,"raw",pair.Key))!=pair.Value)throw new IOException("Export copy hash mismatch: "+pair.Key);
                if(saved)CheckSources(sources,sourceDirectory,fileHashes,lengths,modified);request.VerifyRecord?.Invoke();
                File.WriteAllBytes(Path.Combine(stage,"manifest.json"),manifestBytes);
                File.WriteAllBytes(Path.Combine(stage,"COMPLETE.json"),Json(new{schema_version="researchflow.bundle-complete.1",bundle_id=bundleId,manifest_sha256=Hash(manifestBytes)}));
                Directory.Move(stage,final);
            }
            else
            {
                if(!File.Exists(Path.Combine(final,"COMPLETE.json"))||Hash(File.ReadAllBytes(Path.Combine(final,"manifest.json")))!=Hash(manifestBytes))throw new InvalidDataException("Existing immutable export differs");
                if((string)JObject.Parse(File.ReadAllText(Path.Combine(final,"COMPLETE.json")))["manifest_sha256"]!=Hash(manifestBytes))throw new InvalidDataException("Existing completion marker differs");
                foreach(var pair in fileHashes)if(HashFile(Path.Combine(final,"raw",pair.Key))!=pair.Value)throw new InvalidDataException("Existing raw export hash differs");
            }
            if(saved)CheckSources(sources,sourceDirectory,fileHashes,lengths,modified);request.VerifyRecord?.Invoke();
            string job=Path.Combine(queue,bundleId+".json");
            if(!File.Exists(job))
            {
                string temporary=job+".tmp_"+Guid.NewGuid().ToString("N");
                File.WriteAllBytes(temporary,Json(new{schema_version="researchflow.export_queue.1",bundle_id=bundleId,status="queued",participant_code=request.Participant,session_id=request.Session,run_id=request.Run,group=request.Group,export_path=final.Replace('\\','/'),lifecycle,reason,created_utc=request.CreatedUtc,delivery_authority="verified PC bundle receipt; this file alone is not delivery"}));
                File.Move(temporary,job);
            }
            return final;
            }
            finally{foreach(var stream in sources.Values)stream.Dispose();}
        }
        /// <summary>SHA-256 of the first <paramref name="length"/> bytes, also copied to <paramref name="output"/> when one is given.</summary>
        static string Prefix(FileStream file,long length,Stream output)
        {
            file.Position=0;var buffer=new byte[65536];
            using(var sha=SHA256.Create())
            {
                for(long left=length;left>0;)
                {
                    int read=file.Read(buffer,0,(int)Math.Min(buffer.Length,left));
                    if(read<=0)throw new IOException("Source ended before the frozen snapshot length");
                    sha.TransformBlock(buffer,0,read,null,0);output?.Write(buffer,0,read);left-=read;
                }
                sha.TransformFinalBlock(buffer,0,0);
                return string.Concat(sha.Hash.Select(b=>b.ToString("x2",CultureInfo.InvariantCulture)));
            }
        }
        static void CheckSources(Dictionary<string,FileStream> sources,string directory,Dictionary<string,string> hashes,Dictionary<string,long> lengths,Dictionary<string,DateTime> modified)
        {
            foreach(var pair in sources)
                if(pair.Value.Length!=lengths[pair.Key]||File.GetLastWriteTimeUtc(Path.Combine(directory,pair.Key))!=modified[pair.Key]||Prefix(pair.Value,lengths[pair.Key],null)!=hashes[pair.Key])
                    throw new IOException("Source changed during snapshot: "+pair.Key);
        }
    }
}
