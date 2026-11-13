using Azurite.Core;
using AzureArchive.Automation;

static partial class Program
{
    static void ResourceAvailability()
    {
        var controls=new (string Name,string Json)[]
        {
            ("available false","{\"resource_id\":\"fixture:transition\",\"kind\":\"transition\",\"catalog_version\":\"fixture-catalog\",\"available\":false}"),
            ("missing available","{\"resource_id\":\"fixture:transition\",\"kind\":\"transition\",\"catalog_version\":\"fixture-catalog\"}"),
            ("string availability","{\"resource_id\":\"fixture:transition\",\"kind\":\"transition\",\"catalog_version\":\"fixture-catalog\",\"available\":\"true\"}"),
            ("wrong resource ID","{\"resource_id\":\"other\",\"kind\":\"transition\",\"catalog_version\":\"fixture-catalog\",\"available\":true}"),
            ("wrong resource kind","{\"resource_id\":\"fixture:transition\",\"kind\":\"sound\",\"catalog_version\":\"fixture-catalog\",\"available\":true}"),
            ("wrong catalog version","{\"resource_id\":\"fixture:transition\",\"kind\":\"transition\",\"catalog_version\":\"other\",\"available\":true}")
        };
        foreach(var control in controls)Test("last native Resolve rejects "+control.Name+" before Check/Apply",()=>{
            var host=Host();var plan=Plan(host);var native=AuthoringEditorSession.Current!;
            AuthoringResourceCatalog.ResponseOverride=control.Json;
            var result=host.Commit(plan,7);
            Check(AuthoringResourceCatalog.Resolved.SequenceEqual(new[]{"fixture:transition"}),"negative control did not reach final native Resolve");
            Check(result.Disposition==CardPackV2CommitDisposition.RejectedBeforeMutation && native.Checks==0 && native.Applies==0,
                $"invalid Resolve entered native mutation boundary: {result.Disposition}, Check={native.Checks}, Apply={native.Applies}");
        });
        Test("matching available native Resolve permits the one guarded Apply",()=>{
            var host=Host();var result=host.Commit(Plan(host),7);var native=AuthoringEditorSession.Current!;
            Check(result.Disposition==CardPackV2CommitDisposition.Applied && native.Checks==1 && native.Applies==1,result.Message);
        });
    }
}
