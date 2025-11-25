ore-silo-ui-title = Material Silo
ore-silo-ui-label-clients = Machines
ore-silo-ui-label-mats = Materials
ore-silo-ui-itemlist-entry = {$linked ->
    [true] {"[Linked] "}
    *[False] {""}
} {$name} ({$beacon}) {$inRange ->
    [true] {""}
    *[false] (Out of Range)
}
ore-silo-ui-itemlist-entry-beaconless = {$linked ->
    [true] {"[Linked] "}
    *[False] {""}
} {$name} {$inRange ->
    [true] {""}
    *[false] (Out of Range)
}
