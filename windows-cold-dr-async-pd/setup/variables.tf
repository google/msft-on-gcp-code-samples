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

variable "app-prod-east-tpl-self-link" {
  type        = string
  description = "The self link to the app server template"
}

variable "app-prod-east-service-account" {
  type        = string
  description = "The self link to the app server template"
}

variable "app-prod-east-service-account-scopes" {
  type        = string
  description = "The self link to the app server template"
}

variable "app-prod-east-network-subnet" {
  type        = string
  description = "The self link to the app server template"
}

variable "app-prod-east-ip-subnet" {
  type        = string
  description = "The subnet in East4 to associate the production app server IP address resources with"
}

variable "app-prod-east-region" {
  type        = string
  description = "The region to store the production app server IP address resources"
}

variable "app-dr-east-project" {
  type        = string
  description = "The DR project to contain the secondary boot disks"
}

variable "app-prod-east-project" {
  type        = string
  description = "The project containing the app server primary boot disks"
}

variable "app-dr-region" {
  type        = string
  description = "The DR project to create the secondary boot disks in"
}

variable "app-dr-dc-zone" {
  type        = string
  description = "The zone to contain the domain controller secondary boot disk"
}

variable "app-dr-east-dc-gce-display-name" {
  type        = string
  description = "The DR east domain controller name in the GCE console"
}

variable "prod-east-domain-controller-disk-selflink" {
  type        = string
  description = "The prod east domain controller boot disk self link"
}

variable "app-dr-east-dc-pri-boot-disk-selflink" {
  type        = string
  description = "The east domain controller primary boot disk self link"
}

variable "app-east-dc-disk-type" {
  type        = string
  description = "The DR domain controller secondary boot disk type"
}