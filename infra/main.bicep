targetScope = 'subscription'

@minLength(1)
@maxLength(64)
param environmentName string

param location string = 'australiaeast'

var tags = {
  'azd-env-name': environmentName
  project: 'stallions-nominations-marketplace'
}

var resourceGroupName = 'rg-stallions-noms-${environmentName}'

resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}
