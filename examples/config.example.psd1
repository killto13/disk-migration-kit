@{
    # Target disk identity. All four values must match the physical disk.
    TargetDiskNumber = 2
    TargetModel      = '<TARGET_MODEL>'
    TargetSerial     = '<TARGET_SERIAL>'
    TargetFirmware   = '<TARGET_FIRMWARE>'

    # Source volume and staging/recovery paths.
    SourceVolume     = 'E:'
    StagingRoot      = '<STAGING_ROOT>'          # e.g. '<STAGING_VOLUME>:\E_DISK_MIGRATION'
    RecoveryRoot     = '<RECOVERY_ROOT>'         # e.g. '<RECOVERY_VOLUME>:\E_DISK_RECOVERY'

    # Target basic volume.
    TargetVolume     = 'E:'
    TargetLabel      = 'E'
    TargetFileSystem = 'NTFS'

    # Optional: expected key file and SHA-256 for a quick post-check.
    KeyFile          = ''
    KeyFileSha256    = ''

    # Safety: dry-run is the default; -Execute is still required by the script.
    CancelFlag       = '<AUTOMATION_ROOT>\CANCEL_MIGRATION.flag'
}
