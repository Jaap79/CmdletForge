param(
    [Parameter(Mandatory = $true)]
    [string] $ComputerName,

    [switch] $IncludeServices,

    [bool] $Detailed,

    [string[]] $Tags,

    [int] $TimeoutSeconds = 30,

    [securestring] $ApiToken
)

[pscustomobject]@{
    ComputerName   = $ComputerName
    IncludeServices = $IncludeServices
    Detailed       = $Detailed
    Tags           = $Tags
    TimeoutSeconds = $TimeoutSeconds
}
