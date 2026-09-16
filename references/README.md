# Local reference corpus

The repositories listed in manifest.json are cloned here only for source-level research. The directory is gitignored so upstream source cannot accidentally enter the product repository. Recreate or refresh it with:

    powershell -ExecutionPolicy Bypass -File .\scripts\sync-references.ps1

The sync script checks the remote URL, fetches the exact manifest commit, and checks it out detached. It does not overwrite local modifications; remove or quarantine a modified reference checkout before retrying.
