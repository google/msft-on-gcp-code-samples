/**
 * Copyright 2023 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
 
resource "google_compute_disk_async_replication" "failback-east-async-replication" {
  for_each     = local.sec_boot_disks_for_failback_east
  depends_on   = [google_compute_disk.failback-sec-boot-disks-for-east-vms]
  primary_disk = "projects/${var.app-dr-east-project}/zones/${each.value.dr_disk_zone}/disks/${each.value.dr_disk}"
  secondary_disk {
    disk = google_compute_disk.failback-sec-boot-disks-for-east-vms[each.key].id
  }
}

resource "google_compute_disk_async_replication" "failback-async-replication-for-east-dc" {
  depends_on   = [google_compute_disk.failback-sec-boot-disk-for-east-dc]
  primary_disk = var.dr-east-dc-pri-boot-disk-selflink
  secondary_disk {
    disk = google_compute_disk.failback-sec-boot-disk-for-east-dc.id
  }
}
