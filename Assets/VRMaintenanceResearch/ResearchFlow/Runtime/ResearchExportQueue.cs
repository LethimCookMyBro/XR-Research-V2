using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRMaintenanceResearch.HandsLabQuest3.Logging;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Main-thread immutable snapshot. PC receipt/QC, not this queue, proves delivery.</summary>
    public static class ResearchExportQueue
    {
        static readonly string[] Files={"events.jsonl","movement.csv","session_metadata.json","research-session.json"};
        static readonly UTF8Encoding Utf8=new UTF8Encoding(false);
        static string Hash(byte[] bytes){using(var sha=SHA256.Create())return string.Concat(sha.ComputeHash(bytes).Select(b=>b.ToString("x2",CultureInfo.InvariantCulture)));}
        static string HashStream(FileStream file){file.Position=0;using(var sha=SHA256.Create())return string.Concat(sha.ComputeHash(file).Select(b=>b.ToString("x2",CultureInfo.InvariantCulture)));}
        static string HashFile(string path){using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))return HashStream(file);}
        static byte[] Json(object value)=>Utf8.GetBytes(JsonConvert.SerializeObject(value,Formatting.Indented));
        static void Token(string value)
        {
            if(string.IsNullOrWhiteSpace(value)||value.Length>160||!value.All(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'))throw new InvalidDataException("Unsafe export identity");
        }
        public static string Snapshot(string dataRoot,HandsLabLogger logger,ParticipantRecord record,string reason,string lifecycle,JObject provenance=null)
        {
            if(logger==null||record==null)throw new ArgumentNullException("Active logger and participant record required");
            Token(record.participant_id);Token(record.session_id);Token(record.run_id);
            if(record.run_id!=logger.RunId)throw new InvalidDataException("Export run identity mismatch");
            if(lifecycle!="finalized"&&lifecycle!="task_finalized"&&lifecycle!="partial")throw new ArgumentException("Explicit export lifecycle required");
            if(lifecycle=="finalized"&&!logger.DataComplete)throw new InvalidOperationException("Logger has not finalized; use partial snapshot");
            if(ResearchParticipantRegistry.IsTechnical(record.group)!=record.test_data)throw new InvalidDataException("Export group/provenance mismatch");
            return SnapshotFiles(dataRoot,logger.DirectoryPath,record,reason,lifecycle,provenance,false,null);
        }
        public static string SnapshotSavedRun(ResearchParticipantRegistry registry,string participant,string session,string run,string activeRun=null)
        {
            if(registry==null)throw new ArgumentNullException(nameof(registry));
            Token(participant);Token(session);Token(run);
            if(run==activeRun)throw new InvalidOperationException("Current run cannot use saved-run export");
            Func<ParticipantRecord> resolve=()=>new[]{ParticipantGroup.Dev,ParticipantGroup.Test,ParticipantGroup.Pilot}
                .SelectMany(registry.List).SingleOrDefault(r=>r.participant_id==participant&&r.session_id==session&&r.run_id==run);
            var record=resolve();
            if(record==null||!record.test_data)throw new InvalidDataException("Exact saved technical participant/session/run required");
            byte[] savedRecord=Json(record);
            Action verifyRecord=()=>{var fresh=resolve();if(fresh==null||!Json(fresh).SequenceEqual(savedRecord))throw new IOException("Saved registry record changed during snapshot");};
            var provenance=JObject.FromObject(new{device="unknown",render="unknown",simulated_presence="unknown",apk_sha256=(string)null,source_code_version=(string)null,hardware_verified=false,
                saved_run=true,historical_build_provenance="unknown_pre_upgrade",validity="preserved_raw_pending_host_QC"});
            return SnapshotFiles(registry.DirectoryPath,Path.Combine(registry.DirectoryPath,"Logs",run),record,"operator_export_saved","partial",provenance,true,verifyRecord);
        }
        static string SnapshotFiles(string dataRoot,string sourceDirectory,ParticipantRecord record,string reason,string lifecycle,JObject provenance,bool saved,Action verifyRecord)
        {
            var sources=new Dictionary<string,FileStream>();
            try
            {
            foreach(string file in Files.Take(3))sources[file]=new FileStream(Path.Combine(sourceDirectory,file),FileMode.Open,FileAccess.Read,saved?FileShare.Read:FileShare.ReadWrite);
            if(saved)
            {
                sources[Files[2]].Position=0;JObject metadata;
                using(var reader=new StreamReader(sources[Files[2]],Utf8,false,4096,true))metadata=JObject.Parse(reader.ReadToEnd());
                if((string)metadata["run_id"]!=record.run_id||(bool?)metadata["technical_test"]!=true||(bool?)metadata["test_data"]!=true)throw new InvalidDataException("Saved metadata identity/provenance mismatch");
            }
            var fileHashes=new Dictionary<string,string>();var lengths=new Dictionary<string,long>();
            var modified=new Dictionary<string,DateTime>();
            foreach(string file in Files.Take(3))
            {
                string path=Path.Combine(sourceDirectory,file);
                long length=sources[file].Length;modified[file]=File.GetLastWriteTimeUtc(path);fileHashes[file]=HashStream(sources[file]);lengths[file]=length;
                if(sources[file].Length!=length||File.GetLastWriteTimeUtc(path)!=modified[file])throw new IOException("Snapshot changed while reading: "+file);
            }
            verifyRecord?.Invoke();
            byte[] recordBytes=Json(record);fileHashes[Files[3]]=Hash(recordBytes);lengths[Files[3]]=recordBytes.LongLength;
            var hashes=fileHashes.ToDictionary(pair=>"raw/"+pair.Key,pair=>(object)new{bytes=lengths[pair.Key],sha256=pair.Value});
            var identity=new{participant_code=record.participant_id,session_id=record.session_id,run_id=record.run_id,group=record.group.ToString(),test_data=record.test_data};
            var flags=provenance?.DeepClone()??JObject.FromObject(new{device="unknown",render="unknown",simulated_presence="unknown",apk_sha256=(string)null,source_code_version=(string)null,hardware_verified=false});
            string bundleId="snapshot_"+Hash(Json(new{identity,lifecycle,reason,provenance=flags,files=hashes})).Substring(0,24);
            string exports=Path.Combine(dataRoot,"Exports"),queue=Path.Combine(dataRoot,"ExportQueue");
            Directory.CreateDirectory(exports);Directory.CreateDirectory(queue);
            string final=Path.Combine(exports,bundleId);
            var manifest=new{schema_version="researchflow.device-bundle.1",bundle_id=bundleId,identity,lifecycle,data_completeness="pending_host_QC",delivery_status="queued_for_host",reason,provenance=flags,files=hashes,clock="logger monotonic seconds; native sensor timestamps unavailable",snapshot_policy=saved?"read-only saved run; exclusive read leases and source hashes rechecked; raw bytes preserved":"synchronous main-thread copies of flushed writer files; raw bytes preserved"};
            byte[] manifestBytes=Json(manifest);
            if(!Directory.Exists(final))
            {
                string stage=Path.Combine(exports,".staging_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);Directory.CreateDirectory(Path.Combine(stage,"raw"));
                if(saved)CheckSources(sources,sourceDirectory,fileHashes,lengths,modified);verifyRecord?.Invoke();
                foreach(string file in Files.Take(3))
                {
                    sources[file].Position=0;
                    using(var output=new FileStream(Path.Combine(stage,"raw",file),FileMode.CreateNew,FileAccess.Write,FileShare.Read))sources[file].CopyTo(output,65536);
                }
                File.WriteAllBytes(Path.Combine(stage,"raw",Files[3]),recordBytes);
                foreach(var pair in fileHashes)if(HashFile(Path.Combine(stage,"raw",pair.Key))!=pair.Value)throw new IOException("Export copy hash mismatch: "+pair.Key);
                if(saved)CheckSources(sources,sourceDirectory,fileHashes,lengths,modified);verifyRecord?.Invoke();
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
            if(saved)CheckSources(sources,sourceDirectory,fileHashes,lengths,modified);verifyRecord?.Invoke();
            string job=Path.Combine(queue,bundleId+".json");
            if(!File.Exists(job))
            {
                string temporary=job+".tmp_"+Guid.NewGuid().ToString("N");
                File.WriteAllBytes(temporary,Json(new{schema_version="researchflow.export_queue.1",bundle_id=bundleId,status="queued",participant_code=record.participant_id,session_id=record.session_id,run_id=record.run_id,group=record.group.ToString(),export_path=final.Replace('\\','/'),lifecycle,reason,created_utc=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture),delivery_authority="verified PC bundle receipt; this file alone is not delivery"}));
                File.Move(temporary,job);
            }
            return final;
            }
            finally{foreach(var stream in sources.Values)stream.Dispose();}
        }
        static void CheckSources(Dictionary<string,FileStream> sources,string directory,Dictionary<string,string> hashes,Dictionary<string,long> lengths,Dictionary<string,DateTime> modified)
        {
            foreach(var pair in sources)
                if(pair.Value.Length!=lengths[pair.Key]||File.GetLastWriteTimeUtc(Path.Combine(directory,pair.Key))!=modified[pair.Key]||HashStream(pair.Value)!=hashes[pair.Key])
                    throw new IOException("Source changed during snapshot: "+pair.Key);
        }
    }
}
