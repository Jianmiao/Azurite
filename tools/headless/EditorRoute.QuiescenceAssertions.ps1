# Test-only assertion shared by the real fixture and its mutation checks.
# It cannot admit a production run or change a deadline.
function Assert-EditorRouteQuiescenceFixtureDenial($Observation){
 if($Observation.quiescent -isnot [bool] -or $Observation.quiescent -ne $false -or ($Observation.attemptCount -isnot [int] -and $Observation.attemptCount -isnot [long]) -or $Observation.attemptCount -lt 0){throw 'Fixture expected a denied quiescence record.'}
 $count=@($Observation.samples).Count
 if($Observation.attemptCount -eq 0){
  if($Observation.reason -cne 'shared-deadline-exhausted' -or $count -ne 0){throw 'Zero-attempt denial is valid only for pre-query shared-deadline exhaustion.'}
 }elseif($count -lt 1 -or $count -gt 8){throw 'Queried denial omitted or exceeded bounded member diagnostics.'}
}
