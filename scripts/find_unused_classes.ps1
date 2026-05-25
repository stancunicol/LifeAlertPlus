$root = 'E:\LifeAlertPlus'
Get-ChildItem -Path $root -Recurse -Filter *.cs |
Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
ForEach-Object {
  $file = $_.FullName
  $text = Get-Content $file -Raw
  if ($text -match 'class\s+([A-Za-z0-9_]+)') {
    $name = $matches[1]
    $refs = Select-String -Path "$root\**\*.cs" -Pattern "\b$name\b" -CaseSensitive:$false |
            Select-Object -ExpandProperty Path -Unique |
            Where-Object { $_ -ne $file -and $_ -notmatch '\\(obj|bin)\\' }
    if (($refs | Measure-Object).Count -eq 0) {
      Write-Output "UNUSED:$file -> $name"
    }
  }
}