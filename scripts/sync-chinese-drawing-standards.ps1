[CmdletBinding()]
param(
    [string]$ReferenceRoot,
    [switch]$RefreshOfficialPages
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ReferenceRoot)) {
    $ReferenceRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'references\standards'
}
$root = [IO.Path]::GetFullPath($ReferenceRoot)
$catalogueRoot = Join-Path $root 'catalogue'
$cacheRoot = Join-Path $env:LOCALAPPDATA 'SolidWorksMcp\standards'
New-Item -ItemType Directory -Force -Path $root, $catalogueRoot, $cacheRoot | Out-Null

# Only catalogue metadata and derived compiler policy are tracked. The official site itself explains that many
# adopted GB/T standards cannot be redistributed as full text. 这里只保存题录 metadata 和派生 compiler policy；
# 官方站点明确说明许多采标 GB/T 标准不提供可再分发的全文，因此不把标准正文复制进 public repository。
$standards = @(
    [ordered]@{ id = 'GB/T 14689-2008'; title = '技术制图 图纸幅面和格式'; status = '现行'; effective = '2009-01-01'; url = 'https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=6C3BD0FCD8FFFE7CEC6404FB0180EA96'; adopted = $true },
    [ordered]@{ id = 'GB/T 14690-1993'; title = '技术制图 比例'; status = '现行'; effective = '1994-07-01'; url = 'https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=C111329A862219BCCCAAF32E14FF4CD0'; adopted = $true },
    [ordered]@{ id = 'GB/T 14692-2008'; title = '技术制图 投影法'; status = '现行'; effective = '2009-01-01'; url = 'https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=1072044E4F129FBC0423C354C2D6FE51'; adopted = $true },
    [ordered]@{ id = 'GB/T 10609.1-2008'; title = '技术制图 标题栏'; status = '现行'; effective = '2009-01-01'; url = 'https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=0F8577CB9AB82D5819048A87920CABAB'; adopted = $false },
    [ordered]@{ id = 'GB/T 4458.1-2002'; title = '机械制图 图样画法 视图'; status = '现行'; effective = '2003-04-01'; url = 'https://openstd.samr.gov.cn/bzgk/std/std_list?p.p1=0&p.p2=GBT%204458.1&p.p90=circulation_date&p.p91=desc'; adopted = $true },
    [ordered]@{ id = 'GB/T 4458.3-2013'; title = '机械制图 轴测图'; status = '现行'; effective = '2014-10-01'; url = 'https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=52098A4E44A7F21B33FB11F2609052B4'; adopted = $false },
    [ordered]@{ id = 'GB/T 4458.4-2003'; title = '机械制图 尺寸注法'; status = '现行'; effective = '2003-12-01'; url = 'https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=08588A5F3FE19F16B9EE8D5D87E064D5'; adopted = $false },
    [ordered]@{ id = 'GB/T 4458.5-2003'; title = '机械制图 尺寸公差与配合注法'; status = '现行'; effective = '2003-12-01'; url = 'https://openstd.samr.gov.cn/bzgk/std/std_list?p.p1=0&p.p2=GBT%204458.&p.p90=circulation_date&p.p91=desc'; adopted = $false },
    [ordered]@{ id = 'GB/T 4458.6-2002'; title = '机械制图 图样画法 剖视图和断面图'; status = '现行'; effective = '2003-04-01'; url = 'https://openstd.samr.gov.cn/bzgk/gb/newGbInfo?hcno=98FC4489987C8309C89CC2816AFFCE9C'; adopted = $true },
    [ordered]@{ id = 'GB/T 1800.1-2020'; title = '产品几何技术规范（GPS）线性尺寸公差 ISO 代号体系'; status = '现行'; effective = '2021-03-01'; url = 'https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=B2EA3A6454B903DCF466A0CE16F2ED26'; adopted = $true },
    [ordered]@{ id = 'GB/T 1804-2000'; title = '一般公差 未注公差的线性和角度尺寸的公差'; status = '现行'; effective = '2000-12-01'; url = 'https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=1BF8CFBC644315488F68433EEC2F9D58'; adopted = $false },
    [ordered]@{ id = 'GB/T 1182-2018'; title = '产品几何技术规范（GPS）几何公差'; status = '现行'; effective = '2019-04-01'; url = 'https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=C87A687A21B36E2F2D3A09BDF03DCA01'; adopted = $true }
)

$retrieved = (Get-Date).ToUniversalTime().ToString('O')
$records = foreach ($standard in $standards) {
    $safeName = ($standard.id -replace '[^A-Za-z0-9]+', '-').Trim('-')
    $pagePath = Join-Path $cacheRoot "$safeName.html"
    $pageStatus = 'metadata-only'
    $pageHash = $null
    if ($RefreshOfficialPages) {
        try {
            Invoke-WebRequest -Uri $standard.url -OutFile $pagePath -UseBasicParsing -ErrorAction Stop
            $pageStatus = 'official-page-cached-user-local'
            $pageHash = (Get-FileHash -LiteralPath $pagePath -Algorithm SHA256).Hash
        }
        catch {
            $pageStatus = 'official-page-unavailable'
        }
    }

    [ordered]@{
        standardId = $standard.id
        chineseTitle = $standard.title
        status = $standard.status
        effectiveDate = $standard.effective
        officialUrl = $standard.url
        adoptedFromIsoOrIec = $standard.adopted
        retrievedAtUtc = $retrieved
        officialPageCache = $pageStatus
        officialPageSha256 = $pageHash
        redistribution = 'catalogue metadata and derived RulePack policy only; no standard text or scanned pages'
    }
}

$manifest = [ordered]@{
    schemaVersion = '1.0'
    source = 'National Standard Full Text Publicity System (SAMR/SAC) catalogue metadata'
    generatedAtUtc = $retrieved
    localCacheRoot = $cacheRoot
    standards = @($records)
    derivedPolicy = [ordered]@{
        defaultSheet = 'A4-landscape'
        projection = 'FirstAngle unless enterprise/customer RulePack overrides with provenance'
        titleBlock = 'GB/T 10609.1-2008 metadata reference; native installed GB template required'
        viewLayout = 'Aligned projected views; reserved title-block zone; validate native IView.GetOutline after creation'
        dimensions = 'Do not invent dimensions from appearance; native/model-associated or approved requirement graph only'
        releaseGate = 'Wrong sheet/template, unsupported association, missing requirement coverage, or collision is not Released'
    }
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $root 'manifest.json') -Encoding UTF8

foreach ($record in $records) {
    $safeName = ($record.standardId -replace '[^A-Za-z0-9]+', '-').Trim('-')
    $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $catalogueRoot "$safeName.json") -Encoding UTF8
}

Write-Output "standards-manifest=$([IO.Path]::GetFullPath((Join-Path $root 'manifest.json')) )"
Write-Output "standards-count=$($records.Count)"
Write-Output "standard-text=not-copied"
