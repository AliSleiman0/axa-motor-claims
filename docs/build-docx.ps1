<#
  Builds the client-facing Word document from client-doc-src.html.

  LibreOffice imports HTML as a *web* document, which gets three things wrong for a
  document that will be printed or read in Word: tables are sized to an assumed browser
  viewport (~18% wider than an A4 text column, so the right-hand column is cut off),
  page margins come out at 1cm and asymmetric, and the file is stamped as Word 2007 so
  Word opens it in Compatibility Mode. There is no LibreOffice option that fixes these,
  so this script patches the OOXML afterwards.

  Usage:   pwsh -File docs\build-docx.ps1 -Version 0.2
  Output:  docs\AXA-Motor-Claims-Questions-and-Costs-v<Version>.docx

  Edit content in client-doc-src.html, then re-run. Do NOT hand-edit the .docx and expect
  it to survive the next build.
#>
param(
  [string]$Version = '0.2',
  [string]$Soffice = 'C:\Program Files\LibreOffice\program\soffice.exe'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$WNS   = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
$RNS   = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
$TEXTW = 9638          # A4 (11906 twips) minus 2cm margins each side (1134 x 2)
$MARGIN= 1134

$docs = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $docs 'client-doc-src.html'
$out  = Join-Path $docs "AXA-Motor-Claims-Questions-and-Costs-v$Version.docx"
if (-not (Test-Path $src))     { throw "source not found: $src" }
if (-not (Test-Path $Soffice)) { throw "LibreOffice not found: $Soffice" }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("axadoc-" + [System.Guid]::NewGuid().ToString('N').Substring(0,8))
$bld  = Join-Path $work 'build'
New-Item -ItemType Directory -Path $work -Force | Out-Null

# ---------------------------------------------------------------- 1. HTML -> docx
& $Soffice --headless --norestore --convert-to 'docx:MS Word 2007 XML' --outdir $work $src | Out-Null
$raw = Join-Path $work 'client-doc-src.docx'
if (-not (Test-Path $raw)) { throw 'LibreOffice conversion produced no output' }
[System.IO.Compression.ZipFile]::ExtractToDirectory($raw, $bld)

# ---------------------------------------------------------------- 2. footer part
$footerText = "AXA Motor Claim Management App &#8212; Integration Questions and Running Costs &#8212; v$Version"
$rpr = '<w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:sz w:val="16"/><w:color w:val="767171"/></w:rPr>'
function Fld([string]$instr) {
  "<w:r>$rpr<w:fldChar w:fldCharType=`"begin`"/></w:r><w:r>$rpr<w:instrText xml:space=`"preserve`"> $instr </w:instrText></w:r>" +
  "<w:r>$rpr<w:fldChar w:fldCharType=`"separate`"/></w:r><w:r>$rpr<w:t>1</w:t></w:r><w:r>$rpr<w:fldChar w:fldCharType=`"end`"/></w:r>"
}
$footerXml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
  "<w:ftr xmlns:w=`"$WNS`" xmlns:r=`"$RNS`"><w:p><w:pPr><w:tabs><w:tab w:val=`"right`" w:pos=`"$TEXTW`"/></w:tabs>$rpr</w:pPr>" +
  "<w:r>$rpr<w:t xml:space=`"preserve`">$footerText</w:t></w:r>" +
  "<w:r>$rpr<w:tab/><w:t xml:space=`"preserve`">Page </w:t></w:r>" + (Fld 'PAGE') +
  "<w:r>$rpr<w:t xml:space=`"preserve`"> of </w:t></w:r>" + (Fld 'NUMPAGES') +
  '</w:p></w:ftr>'
[System.IO.File]::WriteAllText((Join-Path $bld 'word\footer1.xml'), $footerXml, (New-Object System.Text.UTF8Encoding($false)))

$relsPath = Join-Path $bld 'word\_rels\document.xml.rels'
$rels = [System.IO.File]::ReadAllText($relsPath)
if ($rels -notmatch 'rIdFtr1') {
  $rels = $rels -replace '</Relationships>', ('<Relationship Id="rIdFtr1" Type="' + $RNS + '/footer" Target="footer1.xml"/></Relationships>')
  [System.IO.File]::WriteAllText($relsPath, $rels)
}
$ctPath = Join-Path $bld '[Content_Types].xml'
$ct = [System.IO.File]::ReadAllText($ctPath)
if ($ct -notmatch 'footer1\.xml') {
  $ct = $ct -replace '</Types>', '<Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/></Types>'
  [System.IO.File]::WriteAllText($ctPath, $ct)
}

# ---------------------------------------------------------------- 3. document.xml
$docPath = Join-Path $bld 'word\document.xml'
$xml = [System.IO.File]::ReadAllText($docPath)
$xml = $xml -replace '<w:pgMar[^/]*/>', "<w:pgMar w:left=`"$MARGIN`" w:right=`"$MARGIN`" w:gutter=`"0`" w:header=`"567`" w:top=`"$MARGIN`" w:footer=`"680`" w:bottom=`"$MARGIN`"/>"
$xml = $xml -replace '<w:sectPr>', '<w:sectPr><w:footerReference w:type="default" r:id="rIdFtr1"/>'
[System.IO.File]::WriteAllText($docPath, $xml)

# Column widths, in document order. Each row MUST sum to $TEXTW.
# Chosen per table rather than scaled proportionally: proportional scaling squeezes the
# leading "#" column below the width of "Q34", which then wraps.
$plan = @(
  @{ n='S3.1 endpoints needed';    w=@(3700,5938) },
  @{ n='S3.2 access+connectivity'; w=@(620,5518,3500) },
  @{ n='S3.3 further NEXT3';       w=@(620,5518,3500) },
  @{ n='S4.2 hosting';             w=@(2400,5138,2100) },
  @{ n='S4.4 mobile distribution'; w=@(6000,3638) }
)

$doc = New-Object System.Xml.XmlDocument
$doc.PreserveWhitespace = $true
$doc.Load($docPath)
$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$ns.AddNamespace('w', $WNS)

$tbls = $doc.SelectNodes('//w:tbl', $ns)
if ($tbls.Count -ne $plan.Count) { throw "table count is $($tbls.Count) but the plan has $($plan.Count) - update `$plan in this script" }

for ($i = 0; $i -lt $tbls.Count; $i++) {
  $tbl = $tbls[$i]; $want = $plan[$i].w
  $cols = $tbl.SelectNodes('w:tblGrid/w:gridCol', $ns)
  if ($cols.Count -ne $want.Count) { throw "table $i ($($plan[$i].n)): $($cols.Count) columns, plan gives $($want.Count)" }
  $sum = ($want | Measure-Object -Sum).Sum
  if ($sum -ne $TEXTW) { throw "table $i ($($plan[$i].n)): widths sum to $sum, not $TEXTW" }

  for ($c = 0; $c -lt $cols.Count; $c++) { $cols[$c].SetAttribute('w', $WNS, [string]$want[$c]) | Out-Null }

  $tblPr = $tbl.SelectSingleNode('w:tblPr', $ns)
  $tw = $tblPr.SelectSingleNode('w:tblW', $ns)
  if ($tw)  { $tw.SetAttribute('w', $WNS, [string]$TEXTW) | Out-Null; $tw.SetAttribute('type', $WNS, 'dxa') | Out-Null }
  $ind = $tblPr.SelectSingleNode('w:tblInd', $ns)
  if ($ind) { $ind.SetAttribute('w', $WNS, '0') | Out-Null }
  $lay = $tblPr.SelectSingleNode('w:tblLayout', $ns)
  if (-not $lay) { $lay = $doc.CreateElement('w','tblLayout',$WNS); $tblPr.AppendChild($lay) | Out-Null }
  $lay.SetAttribute('type', $WNS, 'fixed') | Out-Null

  $rows = $tbl.SelectNodes('w:tr', $ns)
  for ($r = 0; $r -lt $rows.Count; $r++) {
    $tr = $rows[$r]
    $trPr = $tr.SelectSingleNode('w:trPr', $ns)
    if (-not $trPr) { $trPr = $doc.CreateElement('w','trPr',$WNS); $tr.PrependChild($trPr) | Out-Null }
    # keep each row whole rather than letting it break across a page
    if (-not $trPr.SelectSingleNode('w:cantSplit', $ns)) { $trPr.AppendChild($doc.CreateElement('w','cantSplit',$WNS)) | Out-Null }
    # repeat the header row at the top of each page a long table spans
    if ($r -eq 0 -and -not $trPr.SelectSingleNode('w:tblHeader', $ns)) { $trPr.AppendChild($doc.CreateElement('w','tblHeader',$WNS)) | Out-Null }

    $tcs = $tr.SelectNodes('w:tc', $ns)
    for ($c = 0; $c -lt $tcs.Count -and $c -lt $want.Count; $c++) {
      $tcW = $tcs[$c].SelectSingleNode('w:tcPr/w:tcW', $ns)
      if ($tcW) { $tcW.SetAttribute('w', $WNS, [string]$want[$c]) | Out-Null; $tcW.SetAttribute('type', $WNS, 'dxa') | Out-Null }
    }
  }
}

# LibreOffice puts 8pt after every cell paragraph, which makes every row unnecessarily tall
$tightened = 0
foreach ($sp in $doc.SelectNodes('//w:tbl//w:p/w:pPr/w:spacing', $ns)) {
  $sp.SetAttribute('after', $WNS, '40') | Out-Null; $sp.SetAttribute('before', $WNS, '40') | Out-Null; $tightened++
}
$doc.Save($docPath)

# ---------------------------------------------------------------- 4. no Compatibility Mode
$setPath = Join-Path $bld 'word\settings.xml'
$set = [System.IO.File]::ReadAllText($setPath)
$set = $set -replace '(<w:compatSetting w:name="compatibilityMode"[^>]*w:val=")\d+"', '${1}15"'
[System.IO.File]::WriteAllText($setPath, $set)

# ---------------------------------------------------------------- 5. repackage
# [Content_Types].xml must be the first entry in the archive or Word can reject the file.
if (Test-Path $out) { [System.IO.File]::Delete($out) }
$all = Get-ChildItem -Path $bld -Recurse -File | ForEach-Object { $_.FullName.Substring($bld.Length+1) -replace '\\','/' }
$first = @('[Content_Types].xml','_rels/.rels','word/document.xml','word/_rels/document.xml.rels')
$ordered = @($first | Where-Object { $all -contains $_ }) + @($all | Where-Object { $first -notcontains $_ })
$zip = [System.IO.Compression.ZipFile]::Open($out, [System.IO.Compression.ZipArchiveMode]::Create)
foreach ($e in $ordered) {
  [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $bld $e), $e, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
}
$zip.Dispose()
Remove-Item $work -Recurse -Force -Confirm:$false

"built    : $out"
"tables   : $($tbls.Count) resized to $TEXTW twips, header rows repeat, rows kept whole"
"spacing  : $tightened cell paragraphs tightened"
"size     : $((Get-Item $out).Length) bytes"
