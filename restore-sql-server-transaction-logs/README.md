# Restore SQL Server Transaction log files to a Cloud SQL for SQL Server instance



This repository contains the implementation of an python function that restores transaction log backups uploaded to a cloud bucket to a database of an existing Cloud SQL for SQL Server instance.



## Restore functionality


You can import transaction log backups in Cloud SQL for SQL Server since [October 2023](https://cloud.google.com/sql/docs/release-notes#October_17_2023). This functionality helps when migrating to Cloud SQL using backups or setting up Cloud SQL for SQL Server DR instances. 

https://cloud.google.com/sql/docs/sqlserver/import-export/import-export-bak#import_transaction_log_backups



## Main workflow

![Transaction log backup restore to Cloud SQL for SQL Server](tlog-restore-cloudsql.png)

The process starts when SQL Server transaction log backup files are being uploaded to a cloud bucket. These files may come from a SQL Server stand alone instance or CloudSQL for SQL Server.

The upload event fires an EventArc trigger that calls the python function. The function gets the path to the log file that was uploaded and constructs the request to restore the uploaded backup file to the Cloud SQL for SQL Server instance.

After the execution of the import request, the function checks periodically the progress of the restore operation. Once the status of the operation changes to "DONE", which means that it has an outcome, the function executes the following:

1. If the operation returns SUCCESS (determined by the absence of the 'error' element in the response json), then the function makes a copy of the backup file 'processed' storage bucket (if defined) and then finally deletes the file from the source storage bucket, thus signaling that the function processed the file successfully.

1. If the outcome of the operation returns ERROR, then depending on the details of the error response inside, the function implements one of the following decisions:

    * If the import failed with SQL Server error 4326 - too early to apply to the database - then the function assumes that the log file was processed already and deletes it from the bucket. The function returns an OK (200).

    * If the import failed with SQL Server error 4305 - too recent to apply to the database - the function assumes that there are some synchronization and schedules a later restore attempt of the same file, in the same execution run. The MAX_REQUEST_ATTEMPTS configuration parameter defines the maximum number of such retry attempts.  In this case, the function does not delete the file from the source bucket. In this way the function tries to compensate any upload order issues - for example later transaction log backups uploaded before earlier ones.

    * If the import fails for any other reason, the function schedules a later restore attempt in the same execution. The file is not deleted in case of errors.

If the function breaks mid-way, the log backup file will not be deleted from the source bucket (regardless if it was restored to the CloudSQL instance or not). This signifies that it was not processed successfully and that it should be manually scheduled for re-upload.

The function needs certain information to make the proper request to restore the uploaded backup file to the Cloud SQL for SQL Server instance. This information includes:
        - The Cloud SQL Instance name
        - The database name
        - The type of backup (full, differential or transaction log backup)
        - If the backup is restore with recovery or not (leaving the database ready to perform subsequent restores in case of no recovery used)

There are two ways in which the function gets this information: Either from the file name itself or from object metadadata. To enable the function to use the file name functionality, set the USE_FIXED_FILE_NAME_FORMAT environment variable to "True". In this way, the function expects all the uploaded backup files to have a fixed name pattern from which it inferrs the needed information. More information below, in the Constraints section. We recommend using the option that is easier for you to implement (either changing backup file names or deciding the logic to persist object metadata).

The function can also restore full and differential backup files. To achieve this functionality, use the two options provided (fixed file name or object metadata to signal to the function that the backups are full, differential or transaction log backups)
In case of fixed file name, make sure that you have the substrings "_full" or "_diff" in the file name to trigger full respectively diff backup restores.

By default, the function restores backups with the norecovery option, leaving the database in a state to expect further sequential restores. Use the "_recovery" substring in the file name or set the Recovery tag to "True" in the object metadata. This is useful when you need to switching to your DR Cloud SQL instance. In such cases, the function must restore a backup file with the recovery option true. This triggers the recovery option and leaves the database in the accessible state.

This repository also contains a powershell script for regularly uploading new files to cloud storage called upload-script.ps1, existing in the scheduled-upload folder. This provides an automated way of uploading the backup files logs to cloud storage.


The function must have defined a set of environment variables. Details about them are described below, in the constraints section.


## Setup and configuration - Cloud Function


1. Create a GCS bucket to upload your transaction log backup files:

        gcloud storage buckets create gs://<BUCKET_NAME> \
        --project=<project-id> \
        --location=BUCKET_LOCATION \
        --public-access-prevention

1. Use the gcloud describe command to get the service account information of your Cloud SQL Instance

        gcloud sql instances describe <CLOUD_SQL_INSTANCE_NAME>

Copy the value of the serviceAccountEmailAddress field. It should be something in the form of p******@gcp-sa-cloud-sql.iam.gserviceaccount.com.

1. Grant objectViewer rights for the CloudSQL service account on the bucket you just created:

        gsutil iam ch serviceAccount:<service-account-email-address>@gcp-sa-cloud-sql.iam.gserviceaccount.com:legacyBucketReader,objectViewer gs://<BUCKET_NAME>

1. Create a service account for the cloud function:

        gcloud iam service-accounts create cloud-function-sql-restore-log --display-name "Service Account for Cloud Function and SQL Admin API"

1. Create a role called Cloud SQL import that has rights to perform imports on Cloud SQL instances and can also orchestrate files on the buckets:

        export PROJECT_ID=`gcloud config get-value project`

        gcloud iam roles create cloud.sql.importer \
        --project ${PROJECT_ID} \
        --title "Cloud SQL Importer Role" \
        --description "Grant permissions to import and synchronize data from a cloud storage bucket to a Cloud SQL instance" \
        --permissions "cloudsql.instances.get, cloudsql.instances.import, eventarc.events.receiveEvent, storage.buckets.get, storage.objects.create, storage.objects.delete, storage.objects.get"

1. Attach the Cloud SQL import role to the Cloud function service account.

        gcloud projects add-iam-policy-binding ${PROJECT_ID} \
        --member="serviceAccount:cloud-function-sql-restore-log@${PROJECT_ID}.iam.gserviceaccount.com" \
        --role="projects/${PROJECT_ID}/roles/cloud.sql.importer"

1. Deploy the cloud function that triggers when a new object is uploaded to the bucket. The function will restore the full and transaction log backup files and also handle the file synchronization on the bucket.

- On your local development environment, install and initialize the gcloud CLI.

- Clone the sql server restore cloud function repository.

- Navigate to the restore-sql-server-transaction-logs/Function folder

- From the restore-sql-server-transaction-logs/Function folder, run the following gcloud command to deploy the cloud function:

        gcloud functions deploy <YOUR_FUNCTION_NAME> \
        --gen2 \
        --region=<YOUR_REGION> \
        --runtime=<YOUR_RUNTIME> \
        --source=<YOUR_SOURCE_LOCATION> \
        --entry-point=<YOUR_CODE_ENTRYPOINT> \
        --set-env-vars USE_FIXED_FILE_NAME_FORMAT=False,PROCESSED_BUCKET_NAME=,MAX_REQUEST_ATTEMPTS=5,MAX_REQUEST_FETCH_TIME_SECONDS=5,MAX_OPERATION_FETCH_TIME_SECONDS=5
        --service-account cloud-function-sql-restore-log@alexcarciu-alloy-db-testing.iam.gserviceaccount.com

1. To invoke an authenticated cloud function, the underlying principal must have the invoker IAM permission. Assign the Invoker role (roles/run.invoker) through Cloud Run for 2nd gen functions to the function’s service account:

        gcloud functions add-invoker-policy-binding <YOUR_FUNCTION_NAME> \
        --region="<YOUR_REGION>" \
        --member="serviceAccount:cloud-function-sql-restore-log@${PROJECT_ID}.iam.gserviceaccount.com"


## Setup and configuration - Upload script

To be able to run the powershell script on a regular basis, perform the following actions:

1. First, create a service account that has rights to upload to the bucket:

        gcloud iam service-accounts create tx-log-backup-writer \
        --description="Account that writes transaction log backups to GCS" \
        --display-name="tx-log-backup-writer"

1. Grant rights on the service account to view, create and overwrite objects on the bucket:

        gsutil iam ch serviceAccount:tx-log-backup-writer@${PROJECT_ID}.iam.gserviceaccount.com:objectAdmin gs://<BUCKET_NAME>

1. Create a private key for your service account. You need to store the private key file locally to be authorized to upload files to the bucket.

        gcloud iam service-accounts keys create KEY_FILE \
        --iam-account=tx-log-backup-writer@${PROJECT_ID}.iam.gserviceaccount.com

1. Create a folder on a machine with access to the backup files. Place the upload-script.ps1 from the repository in that folder. Copy the key file from the previous step.

1. Open the script and edit the following constants:

Provide in the -Value parameter the full path to the folder where your backup files will be generated:

        New-Variable -Name LocalPathForBackupFiles -Value "" -Option Constant

Provide in the -Value parameter the name of the bucket where you want the backup files to be uploaded:

        New-Variable -Name BucketName -Value "" -Option Constant

Provide in the -Value parameter the full path to the key file that you generated earlier and saved locally on the machine where the script runs:

        New-Variable -Name GoogleAccountKeyFile -Value "" -Option Constant

1. Execute a command to create a scheduled task in Windows to run it on a regular basis. For example, the scheduled task below script starts execution at 2:45 PM and runs every 1 minute.


    schtasks /create /sc minute /mo 1 /tn "GCS Upload script" /tr "powershell <script_full_path>" /st 14:45 /ru <local_account_username> /rp 


Replace <script_full_path> with the path to your powershell script and <username> with a local user account with privileges to read and edit the settings.json files on your machine. You will be prompted to provide the password for the local <local_account_username> when you create the task.

The powershell script runs every 1 minute uploads only new backup files from your specified folder cloud storage. The tracking is kept in the log.json file (created by the script).

You can monitor the execution of the script and set up alerting based on execution count. For example, you can set up an alert if the function did not execute successfully in the last 5 minutes.

For more information about service account keys, see [Create a service account key](https://cloud.google.com/iam/docs/keys-create-delete#creating) and [Best practices for managing service account keys](https://cloud.google.com/iam/docs/best-practices-for-managing-service-account-keys).



## Constraints and working assumptions:


1. The transaction log backup files are uploaded on a continuous, regular and batched manner to the cloud storage bucket. Ideally, the upload of the transaction log files happens in the same order as their creation time so that there is a constant, ordered stream of files being uploaded. In case of multiple files uploaded at the same time, race conditions could happen and some backups might not be processed as they are too late for restore.

1. The function needs certain information to make the proper request to restore the uploaded backup file to the Cloud SQL for SQL Server instance. This information includes the Cloud SQL Instance name, database name, the type of backup (full, differential or transaction log backup) and if the backup is restored with recovery or not. There are two ways in which the function gets the necessary this information:
        
- From the file name itself. To enable this functionality, set the USE_FIXED_FILE_NAME_FORMAT environment variable to "True". In this way, the function expects all the uploaded backup files to have a fixed name pattern from which it inferrs the needed information. The fixed file name pattern is:

        <cloud-sql-instance-name>_<database-name>_[<backup_type>]_[<recovery>].*

- [cloud-sql-instance-name] - is the name of the cloud sql for sql server instance where the restore request is made. Mandatory when useing fixed file name option.
- [database-name] - is the name of the database where the function executes the restore operation. Mandatory when useing fixed file name option.

- [backup_type] - Optional. If the function finds the substring "_full" or "_diff" in the file name, it will execute a full respectively a diff restore. If there is no such substring, the function performs the default TLOG restore.

- [recovery] - Optional. If the function finds the substring "_recovery" in the file name, it will execute the restore with the recovery option enabled. This will recover the database and leave it in an accessible state. However, no subsequent backups can be restored to the database. If there is no such substring, the function performs the default restore with no recovery, allowing subsequent restores to be applied to the database

Note that when using this option, the function ignores any object metadata.

- Using metadata. To enable this functionality, set the USE_FIXED_FILE_NAME_FORMAT environment variable to "False". The function gets information about the restore from object metadata. The following metadata tags are expected:
        
- [CloudSqlInstance] - This is the name of the cloud sql for sql server instance where the restore request is made. Mandatory.

- [DatabaseName] - This the name of the database where the function executes the restore operation. Mandatory.

- [BackupType] - This is the backup type. Can be only "FULL", "DIFF" or "TLOG". Mandatory.

- [Recovery] - This is the recovery type. Can be only "True" or "False". Mandatory.


When performing the upload to the GCS bucket, construct logic to set metadata tags. In the provided powershell script, the template to place this logic is provided through functions.

Note that when using this option, the function completly ignores the backup file names.

The upload script provides functions where logic can be placed to define the value of the metadata tags, when using this option.

1. The service account role of the Cloud SQL Instance must have objectViewer rights on the source bucket.

1. The service account of the Cloud function has the following rights to import data from a cloud storage bucket to a Cloud SQL instance and to synchronize backup files on the upload and processed storage buckets:

    - cloudsql.instances.get - to query information from the Cloud SQL instance
    - cloudsql.instances.import - to request a backup import(restore) to the Cloud SQL instance
    - eventarc.events.receiveEvent - to be able to recevie an eventarc trigger event
    - storage.buckets.get - to be able to get information about storage buckets
    - storage.objects.create - to be able to create objects a google cloud storage bucket
    - storage.objects.delete - to be able to delete (overwrite) objects on a google cloud storage bucket
    - storage.objects.get - to be able to read objects on a google cloud storage bucket


## References


* [Eventarc triggers](https://cloud.google.com/functions/docs/calling/eventarc)

* [Deploy a Cloud Function](https://cloud.google.com/functions/docs/deploy)

* [Import data from a BAK file to Cloud SQL for SQL Server](https://cloud.google.com/sql/docs/sqlserver/import-export/import-export-bak#import_data_from_a_bak_file_to)

* [Recovery and the transaction log](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/restore-and-recovery-overview-sql-server?view=sql-server-ver16#TlogAndRecovery)
